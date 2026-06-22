using CodeMeridian.Core.CodeGraph;
using Neo4j.Driver;

namespace CodeMeridian.Infrastructure.Graph;

public sealed partial class Neo4jCodeGraphRepository
{
    public async Task<IReadOnlyList<EndpointTracePath>> FindEndpointTracesAsync(
        string endpointRoute,
        string? projectContext = null,
        int maxDepth = 10,
        CancellationToken cancellationToken = default)
    {
        var clampedDepth = Math.Clamp(maxDepth, 1, 12);
        await using var session = _driver.AsyncSession();

        var cypher = $$"""
            MATCH (endpoint:CodeNode)
            WHERE endpoint.type = 'ApiEndpoint'
              AND ($projectContextNormalized IS NULL OR endpoint.projectContextNormalized = $projectContextNormalized)
              AND (
                    endpoint.name = $endpointRoute
                 OR toLower(endpoint.name) = toLower($endpointRoute)
                 OR endpoint.id = $endpointRoute
                 OR endpoint.id ENDS WITH $endpointSuffix
              )
            MATCH (target:CodeNode)
            WHERE ($projectContextNormalized IS NULL OR target.projectContextNormalized = $projectContextNormalized)
              AND (
                    target.type IN ['DatabaseTable', 'MessageTopic']
                 OR (target.type = 'ExternalConcept' AND target.externalKind = 'DatabaseTable')
              )
            MATCH path = shortestPath((endpoint)-[:{{ConnectionRelationships}}*1..{{clampedDepth}}]-(target))
            RETURN [n IN nodes(path) | n] AS pathNodes,
                   [r IN relationships(path) | { type: type(r), confidence: r.confidence }] AS pathRelationships
            ORDER BY length(path) ASC, target.name
            LIMIT 20
            """;

        var cursor = await session.RunAsync(cypher, new
        {
            endpointRoute,
            endpointSuffix = $"::ApiEndpoint::{endpointRoute}",
            projectContextNormalized = (object?)Normalize(projectContext)
        });

        var results = new List<EndpointTracePath>();
        await foreach (var record in cursor.WithCancellation(cancellationToken))
        {
            var pathNodes = record["pathNodes"].As<List<INode>>();
            var pathRelationships = record["pathRelationships"].As<List<object>>();
            var steps = new List<GraphPathStep>(pathNodes.Count);

            for (var i = 0; i < pathNodes.Count; i++)
            {
                string? relationshipType = null;
                double? relationshipConfidence = null;

                if (i < pathRelationships.Count && pathRelationships[i] is IDictionary<string, object> rel)
                {
                    relationshipType = rel.TryGetValue("type", out var typeValue)
                        ? typeValue?.ToString()
                        : null;

                    if (rel.TryGetValue("confidence", out var confidenceValue) && confidenceValue is not null)
                    {
                        relationshipConfidence = confidenceValue switch
                        {
                            double value => value,
                            float value => value,
                            long value => value,
                            int value => value,
                            _ when double.TryParse(confidenceValue.ToString(), out var parsed) => parsed,
                            _ => null
                        };
                    }
                }

                steps.Add(new GraphPathStep(
                    MapToCodeNode(pathNodes[i]),
                    relationshipType,
                    relationshipConfidence));
            }

            results.Add(new EndpointTracePath(steps));
        }

        return results;
    }
}
