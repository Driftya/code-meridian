using CodeMeridian.Core.Knowledge;
using CodeMeridian.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using Neo4j.Driver;

namespace CodeMeridian.Infrastructure.Graph;

public sealed class Neo4jChangeContextRepository : IChangeContextRepository, IAsyncDisposable
{
    private readonly IDriver _driver;

    public Neo4jChangeContextRepository(IOptions<Neo4jOptions> options)
    {
        var configured = options.Value;
        _driver = GraphDatabase.Driver(
            configured.Uri,
            AuthTokens.Basic(configured.Username, configured.Password));
    }

    public async Task UpsertAsync(ChangeContextEntry context, CancellationToken cancellationToken = default)
    {
        await using var session = _driver.AsyncSession();

        const string cypher = """
            MATCH (target:CodeNode {id: $nodeId})
            MERGE (context:KnowledgeDocument:HumanCognitiveSeedContext {id: $id})
            ON CREATE SET context.createdAt = $createdAt,
                          context.content = $statement,
                          context.source = $source,
                          context.projectContext = $projectContext,
                          context.projectContextNormalized = $projectContextNormalized,
                          context.metadataKind = $metadataKind,
                          context.contextKind = $contextKind,
                          context.provenance = $provenance,
                          context.userConfirmed = $userConfirmed,
                          context.targetNodeId = $nodeId,
                          context.targetSourceHashAtWrite = $targetSourceHashAtWrite,
                          context.targetUpdatedAtAtWrite = $targetUpdatedAtAtWrite,
                          context.contentHash = $contentHash,
                          context.relatedNodeIds = $nodeId,
                          context.updatedAt = $updatedAt
            MERGE (context)-[:Mentions]->(target)
            RETURN context.id AS id
            """;

        var cursor = await session.RunAsync(cypher, new
        {
            id = context.Id,
            nodeId = context.NodeId,
            statement = context.Statement,
            source = context.Id,
            projectContext = context.ProjectContext,
            projectContextNormalized = context.ProjectContext.ToLowerInvariant(),
            metadataKind = ChangeContextEntry.MetadataKind,
            contextKind = context.ContextKind,
            provenance = context.Provenance,
            userConfirmed = context.UserConfirmed,
            targetSourceHashAtWrite = context.TargetSourceHashAtWrite,
            targetUpdatedAtAtWrite = context.TargetUpdatedAtAtWrite?.ToUnixTimeMilliseconds(),
            contentHash = context.ContentHash,
            createdAt = context.CreatedAt.ToUnixTimeMilliseconds(),
            updatedAt = context.CreatedAt.ToUnixTimeMilliseconds()
        });

        var persisted = false;
        await foreach (var _ in cursor.WithCancellation(cancellationToken))
            persisted = true;

        if (!persisted)
            throw new InvalidOperationException($"Code node '{context.NodeId}' no longer exists.");
    }

    public async Task<IReadOnlyList<ChangeContextEntry>> ListForNodeAsync(
        string nodeId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        await using var session = _driver.AsyncSession();

        const string cypher = """
            MATCH (context:KnowledgeDocument:HumanCognitiveSeedContext {targetNodeId: $nodeId})
            WHERE context.metadataKind = $metadataKind
            RETURN context
            ORDER BY context.createdAt DESC, context.id
            LIMIT $limit
            """;

        var cursor = await session.RunAsync(cypher, new
        {
            nodeId,
            metadataKind = ChangeContextEntry.MetadataKind,
            limit
        });
        var results = new List<ChangeContextEntry>();

        await foreach (var record in cursor.WithCancellation(cancellationToken))
            results.Add(Map(record["context"].As<INode>()));

        return results;
    }

    public async ValueTask DisposeAsync() => await _driver.DisposeAsync();

    private static ChangeContextEntry Map(INode node)
    {
        var properties = node.Properties;
        return new ChangeContextEntry
        {
            Id = properties["id"].As<string>(),
            NodeId = properties["targetNodeId"].As<string>(),
            Statement = properties["content"].As<string>(),
            ContextKind = properties["contextKind"].As<string>(),
            Provenance = properties["provenance"].As<string>(),
            UserConfirmed = properties["userConfirmed"].As<bool>(),
            ProjectContext = properties["projectContext"].As<string>(),
            ContentHash = properties["contentHash"].As<string>(),
            TargetSourceHashAtWrite = ReadString(properties, "targetSourceHashAtWrite"),
            TargetUpdatedAtAtWrite = ReadTimestamp(properties, "targetUpdatedAtAtWrite"),
            CreatedAt = ReadTimestamp(properties, "createdAt") ?? DateTimeOffset.UnixEpoch
        };
    }

    private static string? ReadString(IReadOnlyDictionary<string, object?> properties, string key) =>
        properties.TryGetValue(key, out var value) && value is not null ? value.As<string>() : null;

    private static DateTimeOffset? ReadTimestamp(IReadOnlyDictionary<string, object?> properties, string key)
    {
        if (!properties.TryGetValue(key, out var value) || value is null)
            return null;

        var milliseconds = value.As<long?>();
        return milliseconds.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(milliseconds.Value) : null;
    }
}
