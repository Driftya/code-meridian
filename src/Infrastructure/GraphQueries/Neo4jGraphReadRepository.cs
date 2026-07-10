using System.Globalization;
using System.Text.Json;
using CodeMeridian.Core.GraphQueries;
using CodeMeridian.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Neo4j.Driver;

namespace CodeMeridian.Infrastructure.GraphQueries;

public sealed class Neo4jGraphReadRepository : IGraphReadRepository, IAsyncDisposable
{
    private static readonly HashSet<string> OmittedPropertyNames =
    [
        "embedding"
    ];

    private readonly IDriver _driver;
    private readonly ILogger<Neo4jGraphReadRepository> _logger;

    public Neo4jGraphReadRepository(
        IOptions<Neo4jOptions> options,
        ILogger<Neo4jGraphReadRepository> logger)
    {
        _logger = logger;
        var opts = options.Value;
        _driver = GraphDatabase.Driver(
            opts.Uri,
            AuthTokens.Basic(opts.Username, opts.Password),
            config => config
                .WithMaxConnectionPoolSize(opts.MaxConnectionPoolSize)
                .WithConnectionTimeout(TimeSpan.FromSeconds(opts.ConnectionTimeoutSeconds)));
    }

    public async Task<IReadOnlyList<string>> ListLabelsAsync(CancellationToken cancellationToken = default)
    {
        await using var session = _driver.AsyncSession();

        const string cypher = """
            MATCH (n)
            UNWIND labels(n) AS label
            RETURN DISTINCT label
            ORDER BY label
            """;

        var cursor = await session.RunAsync(cypher);
        var labels = new List<string>();

        await foreach (var record in cursor.WithCancellation(cancellationToken))
            labels.Add(record["label"].As<string>());

        return labels;
    }

    public async Task<IReadOnlyList<string>> ListRelationshipTypesAsync(CancellationToken cancellationToken = default)
    {
        await using var session = _driver.AsyncSession();

        const string cypher = """
            MATCH ()-[r]->()
            RETURN DISTINCT type(r) AS relationshipType
            ORDER BY relationshipType
            """;

        var cursor = await session.RunAsync(cypher);
        var relationshipTypes = new List<string>();

        await foreach (var record in cursor.WithCancellation(cancellationToken))
            relationshipTypes.Add(record["relationshipType"].As<string>());

        return relationshipTypes;
    }

    public async Task<GraphNode?> GetNodeAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        await using var session = _driver.AsyncSession();

        const string cypher = """
            MATCH (n)
            WHERE coalesce(n.id, elementId(n)) = $nodeId
            RETURN n, labels(n) AS nodeLabels
            LIMIT 1
            """;

        var cursor = await session.RunAsync(cypher, new { nodeId });
        var records = await cursor.ToListAsync();
        if (records.Count == 0)
            return null;

        return MapNode(
            records[0]["n"].As<INode>(),
            records[0]["nodeLabels"].As<List<string>>());
    }

    public async Task<IReadOnlyList<GraphNode>> QueryNodesAsync(
        GraphNodeFilter filter,
        GraphSort? sort,
        int skip,
        int limit,
        CancellationToken cancellationToken = default)
    {
        await using var session = _driver.AsyncSession();
        var query = Neo4jGraphQueryBuilder.BuildNodeQuery(filter, sort, skip, limit);
        var cursor = await session.RunAsync(query.Cypher, query.Parameters);
        var results = new List<GraphNode>();

        await foreach (var record in cursor.WithCancellation(cancellationToken))
        {
            results.Add(MapNode(
                record["n"].As<INode>(),
                record["nodeLabels"].As<List<string>>()));
        }

        return results;
    }

    public async Task<IReadOnlyList<GraphRelationship>> QueryRelationshipsAsync(
        GraphRelationshipFilter filter,
        GraphSort? sort,
        int skip,
        int limit,
        CancellationToken cancellationToken = default)
    {
        await using var session = _driver.AsyncSession();
        var query = Neo4jGraphQueryBuilder.BuildRelationshipQuery(filter, sort, skip, limit);
        var cursor = await session.RunAsync(query.Cypher, query.Parameters);
        var results = new List<GraphRelationship>();

        await foreach (var record in cursor.WithCancellation(cancellationToken))
        {
            results.Add(MapRelationship(
                record["r"].As<IRelationship>(),
                record["fromNodeId"].As<string>(),
                record["toNodeId"].As<string>()));
        }

        return results;
    }

    public async Task<IReadOnlyList<GraphNeighbor>> GetNeighborsAsync(
        string nodeId,
        IReadOnlyCollection<string> relationshipTypes,
        GraphDirection direction,
        int depth,
        int limit,
        CancellationToken cancellationToken = default)
    {
        await using var session = _driver.AsyncSession();
        var pattern = direction switch
        {
            GraphDirection.Outgoing => $"(start)-[pathRelationships*1..{depth}]->(neighbor)",
            GraphDirection.Incoming => $"(start)<-[pathRelationships*1..{depth}]-(neighbor)",
            _ => $"(start)-[pathRelationships*1..{depth}]-(neighbor)"
        };

        var relationshipFilterClause = relationshipTypes.Count == 0
            ? string.Empty
            : "AND ALL(rel IN pathRelationships WHERE type(rel) IN $relationshipTypes)";

        var cypher = $"""
            MATCH (start)
            WHERE coalesce(start.id, elementId(start)) = $nodeId
            MATCH path = {pattern}
            WHERE coalesce(neighbor.id, elementId(neighbor)) <> $nodeId
              {relationshipFilterClause}
            WITH DISTINCT neighbor, labels(neighbor) AS neighborLabels, last(relationships(path)) AS lastRelationship, length(path) AS distance
            RETURN
                neighbor,
                neighborLabels,
                lastRelationship AS rel,
                coalesce(startNode(lastRelationship).id, elementId(startNode(lastRelationship))) AS fromNodeId,
                coalesce(endNode(lastRelationship).id, elementId(endNode(lastRelationship))) AS toNodeId,
                distance
            ORDER BY distance ASC, coalesce(neighbor.name, neighbor.source, neighbor.value, coalesce(neighbor.id, elementId(neighbor))) ASC
            LIMIT $limit
            """;

        var cursor = await session.RunAsync(cypher, new
        {
            nodeId,
            relationshipTypes = relationshipTypes.ToArray(),
            limit
        });

        var results = new List<GraphNeighbor>();
        await foreach (var record in cursor.WithCancellation(cancellationToken))
        {
            results.Add(new GraphNeighbor
            {
                Node = MapNode(
                    record["neighbor"].As<INode>(),
                    record["neighborLabels"].As<List<string>>()),
                Relationship = MapRelationship(
                    record["rel"].As<IRelationship>(),
                    record["fromNodeId"].As<string>(),
                    record["toNodeId"].As<string>()),
                Distance = record["distance"].As<int>()
            });
        }

        return results;
    }

    public async ValueTask DisposeAsync()
    {
        _logger.LogDebug("Disposing Neo4j graph read repository driver.");
        await _driver.DisposeAsync();
    }

    private static GraphNode MapNode(INode node, IReadOnlyList<string> labels)
    {
        var properties = node.Properties;
        var primaryLabel = labels.Count > 0 ? labels[0] : "Node";

        return new GraphNode
        {
            Id = ReadString(properties, "id") ?? node.ElementId,
            Labels = labels,
            PrimaryLabel = primaryLabel,
            ProjectContext = ReadString(properties, "projectContext"),
            Name = ReadString(properties, "name")
                ?? ReadString(properties, "source")
                ?? ReadString(properties, "value"),
            Type = ReadString(properties, "type") ?? primaryLabel,
            FilePath = ReadString(properties, "filePath"),
            Properties = MapProperties(properties)
        };
    }

    private static GraphRelationship MapRelationship(
        IRelationship relationship,
        string fromNodeId,
        string toNodeId)
    {
        return new GraphRelationship
        {
            Id = ReadString(relationship.Properties, "id") ?? relationship.ElementId,
            Type = relationship.Type,
            FromNodeId = fromNodeId,
            ToNodeId = toNodeId,
            Properties = MapProperties(relationship.Properties)
        };
    }

    private static IReadOnlyList<GraphProperty> MapProperties(IReadOnlyDictionary<string, object> properties)
    {
        return properties
            .Where(pair => !OmittedPropertyNames.Contains(pair.Key))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Take(64)
            .Select(MapProperty)
            .ToArray();
    }

    private static GraphProperty MapProperty(KeyValuePair<string, object> pair)
    {
        var (value, kind) = FormatValue(pair.Value);
        return new GraphProperty
        {
            Key = pair.Key,
            Value = value,
            ValueKind = kind
        };
    }

    private static (string? Value, GraphPropertyValueKind Kind) FormatValue(object? rawValue)
    {
        return rawValue switch
        {
            null => (null, GraphPropertyValueKind.Null),
            string value => (value, GraphPropertyValueKind.String),
            bool value => (value ? "true" : "false", GraphPropertyValueKind.Boolean),
            sbyte or byte or short or ushort or int or uint or long or ulong
                => (Convert.ToString(rawValue, CultureInfo.InvariantCulture), GraphPropertyValueKind.Integer),
            float or double or decimal
                => (Convert.ToString(rawValue, CultureInfo.InvariantCulture), GraphPropertyValueKind.Float),
            Array value => (JsonSerializer.Serialize(value), GraphPropertyValueKind.Array),
            IEnumerable<object> value => (JsonSerializer.Serialize(value), GraphPropertyValueKind.Array),
            _ => (rawValue.ToString(), GraphPropertyValueKind.Object)
        };
    }

    private static string? ReadString(IReadOnlyDictionary<string, object> properties, string key)
    {
        return properties.TryGetValue(key, out var value)
            ? value?.ToString()
            : null;
    }
}
