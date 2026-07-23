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
            MATCH (handler:CodeNode)-[endpointRel:Uses]->(endpoint)
            WHERE ($projectContextNormalized IS NULL OR handler.projectContextNormalized = $projectContextNormalized)
            CALL {
                WITH endpoint, endpointRel, handler
                MATCH path = (handler)-[:Calls|Uses|Reads|Writes|PublishesTo|SubscribesTo*1..{{clampedDepth}}]->(target:CodeNode)
                WHERE all(node IN nodes(path) WHERE $projectContextNormalized IS NULL OR node.projectContextNormalized = $projectContextNormalized)
                  AND (
                        target.type = 'MessageTopic'
                     OR (
                            (target.type = 'DatabaseTable' OR (target.type = 'ExternalConcept' AND target.externalKind = 'DatabaseTable'))
                        AND any(operation IN nodes(path) WHERE operation.type = 'ExternalConcept' AND operation.externalKind = 'DatabaseOperation')
                        AND all(operation IN [node IN nodes(path) WHERE node.type = 'ExternalConcept' AND node.externalKind = 'DatabaseOperation']
                                WHERE coalesce(toFloat(operation.recognitionConfidence), 1.0) >= 0.8)
                     )
                  )
                RETURN [endpoint] + nodes(path) AS pathNodes,
                       [endpointRel] + relationships(path) AS pathRels,
                       target.name AS targetName
                UNION
                WITH endpoint, endpointRel, handler
                MATCH prefix = (handler)-[:Calls|Uses|Reads|Writes|PublishesTo|SubscribesTo*0..{{clampedDepth}}]->(contract:CodeNode)
                MATCH (implementation:CodeNode)-[implementationRel:Implements]->(contract)
                MATCH suffix = (implementation)-[:Calls|Uses|Reads|Writes|PublishesTo|SubscribesTo*1..{{clampedDepth}}]->(target:CodeNode)
                WITH endpoint, endpointRel, prefix, implementationRel, implementation, suffix, target,
                     nodes(prefix) + [implementation] + tail(nodes(suffix)) AS executionNodes
                WHERE all(node IN executionNodes WHERE $projectContextNormalized IS NULL OR node.projectContextNormalized = $projectContextNormalized)
                  AND (
                        target.type = 'MessageTopic'
                     OR (
                            (target.type = 'DatabaseTable' OR (target.type = 'ExternalConcept' AND target.externalKind = 'DatabaseTable'))
                        AND any(operation IN executionNodes WHERE operation.type = 'ExternalConcept' AND operation.externalKind = 'DatabaseOperation')
                        AND all(operation IN [node IN executionNodes WHERE node.type = 'ExternalConcept' AND node.externalKind = 'DatabaseOperation']
                                WHERE coalesce(toFloat(operation.recognitionConfidence), 1.0) >= 0.8)
                     )
                  )
                RETURN [endpoint] + executionNodes AS pathNodes,
                       [endpointRel] + relationships(prefix) + [implementationRel] + relationships(suffix) AS pathRels,
                       target.name AS targetName
            }
            RETURN pathNodes,
                   [r IN pathRels | { type: type(r), confidence: r.confidence }] AS pathRelationships
            ORDER BY size(pathNodes) ASC, targetName
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
