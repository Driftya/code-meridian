using CodeMeridian.Core.Knowledge;
using Neo4j.Driver;

namespace CodeMeridian.Infrastructure.Knowledge;

public sealed partial class Neo4jVectorRepository
{
    public async Task<IReadOnlyList<KnowledgeDocument>> SearchByTextInScopeAsync(string query, string projectContext,
        DocumentQueryScope scope, int topK = 10, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(projectContext) || projectContext.Length > 200
            || projectContext != projectContext.Trim() || projectContext.Any(char.IsControl))
            throw new ArgumentException("A valid project context is required for scoped document search.", nameof(projectContext));
        ArgumentNullException.ThrowIfNull(scope);
        await using var session = _driver.AsyncSession();
        const string cypher = """
            CALL db.index.fulltext.queryNodes('knowledge_fulltext', $query)
            YIELD node, score
            WITH node, score, replace(node.source, '\\', '/') AS source
            WHERE node.projectContextNormalized = $projectContextNormalized
              AND coalesce(node.metadataKind, '') <> $excludedMetadataKind
              AND ($rootPath = '.' OR source STARTS WITH $rootPrefix)
              AND ($targetPath IS NULL OR source = $targetPath)
            RETURN node, score
            ORDER BY score DESC, node.id
            LIMIT $topK
            """;
        var cursor = await session.RunAsync(cypher, new
        {
            query = Neo4jVectorRepositoryHelpers.EscapeLuceneQuery(query),
            projectContextNormalized = Neo4jVectorRepositoryHelpers.Normalize(projectContext),
            excludedMetadataKind = ChangeContextEntry.MetadataKind,
            rootPath = scope.RootPath, rootPrefix = scope.RootPath + "/", targetPath = scope.TargetPath,
            topK = Math.Clamp(topK, 1, 100)
        });
        var results = new List<KnowledgeDocument>();
        await foreach (var record in cursor.WithCancellation(cancellationToken))
        {
            var document = Neo4jVectorRepositoryHelpers.MapToDocument(record["node"].As<INode>());
            if (scope.Contains(document.Source)) results.Add(document);
        }
        return results;
    }
}
