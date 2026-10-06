using CodeMeridian.Core.CodeGraph;
using Neo4j.Driver;

namespace CodeMeridian.Infrastructure.Graph;

public sealed partial class Neo4jCodeGraphRepository : ISqlGraphRepository
{
    internal async Task InitializeSqlGraphAsync(CancellationToken cancellationToken = default)
    {
        await using var session = _driver.AsyncSession();
        foreach (var query in new[]
        {
            "CREATE CONSTRAINT codenode_id IF NOT EXISTS FOR (n:CodeNode) REQUIRE n.id IS UNIQUE",
            "CREATE CONSTRAINT sql_index_project IF NOT EXISTS FOR (n:SqlIndexState) REQUIRE n.projectContext IS UNIQUE",
            "CREATE INDEX sql_node_project IF NOT EXISTS FOR (n:CodeNode) ON (n.sqlProject)",
            "CREATE INDEX sql_node_owner IF NOT EXISTS FOR (n:CodeNode) ON (n.sqlProject, n.sqlFile)"
        })
        {
            cancellationToken.ThrowIfCancellationRequested();
            await (await session.RunAsync(query)).ConsumeAsync();
        }
    }

    public async Task PublishSqlGraphAsync(SqlGraphSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        if (snapshot.Validate() is { } error) throw new ArgumentException(error, nameof(snapshot));
        await using var session = _driver.AsyncSession();
        await session.ExecuteWriteAsync(async tx =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Acquire a project-local write lock before replacing the complete file inventory.
            await RunSqlAsync(tx, """
                MERGE (state:SqlIndexState {projectContext: $project})
                SET state.revision = coalesce(state.revision, 0) + 1
                """, new { project = snapshot.ProjectContext });
            var paths = snapshot.Files.Select(f => f.Path).ToArray();
            await RunSqlAsync(tx, """
                MATCH (:CodeNode {sqlProject: $project})-[r]->() WHERE r.sqlProject = $project AND NOT r.sqlFile IN $paths DELETE r
                """, new { project = snapshot.ProjectContext, paths });
            await RunSqlAsync(tx, """
                MATCH (n:CodeNode) WHERE n.sqlProject = $project AND n.sqlFile IS NOT NULL AND NOT n.sqlFile IN $paths
                SET n.analysisStatus = 'removed'
                WITH n WHERE NOT (n)--() DELETE n
                """, new { project = snapshot.ProjectContext, paths });
            foreach (var file in snapshot.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (file.Status == "failed")
                {
                    foreach (var node in file.Nodes.Where(n => n.Type == CodeNodeType.File))
                        await RunSqlAsync(tx, """
                            MERGE (n:CodeNode {id: $id})
                            ON CREATE SET n.name = $path, n.nameNormalized = toLower($path), n.type = 'File',
                                n.filePath = $path, n.filePathNormalized = toLower($path),
                                n.projectContext = $project, n.projectContextNormalized = toLower($project),
                                n.sqlProject = $project, n.sqlFile = $path, n.createdAt = $now
                            """, new { id = node.Id, path = file.Path, project = snapshot.ProjectContext, now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });
                    await RunSqlAsync(tx, """
                        MATCH (n:CodeNode) WHERE n.sqlProject = $project AND n.sqlFile = $path
                        SET n.analysisStatus = 'stale', n.analysisDiagnostic = $diagnostic
                        """, new { project = snapshot.ProjectContext, path = file.Path, diagnostic = string.Join("; ", file.Diagnostics) });
                    await RunSqlAsync(tx, """
                        MATCH (:CodeNode {sqlProject: $project})-[r]->() WHERE r.sqlProject = $project AND r.sqlFile = $path SET r.sqlStale = true
                        """, new { project = snapshot.ProjectContext, path = file.Path });
                    continue;
                }
                await RunSqlAsync(tx, """
                    MATCH (:CodeNode {sqlProject: $project})-[r]->() WHERE r.sqlProject = $project AND r.sqlFile = $path DELETE r
                    """, new { project = snapshot.ProjectContext, path = file.Path });
                foreach (var node in file.Nodes)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await UpsertSqlNodeAsync(tx, snapshot.ProjectContext, file, node);
                }
                var ids = file.Nodes.Select(n => n.Id).ToArray();
                await RunSqlAsync(tx, """
                    MATCH (n:CodeNode) WHERE n.sqlProject = $project AND n.sqlFile = $path AND NOT n.id IN $ids
                    SET n.analysisStatus = 'removed'
                    WITH n WHERE NOT (n)--() DELETE n
                    """, new { project = snapshot.ProjectContext, path = file.Path, ids });
                foreach (var edge in file.Edges)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await RunSqlAsync(tx, $$"""
                        MATCH (s:CodeNode {id: $source}), (t:CodeNode {id: $target})
                        CREATE (s)-[r:{{edge.Type}}]->(t)
                        SET r += $properties, r.sqlProject = $project, r.sqlFile = $path,
                            r.sqlStale = false, r.callSite = $callSite, r.confidence = $confidence,
                            r.evidenceKind = $evidenceKind, r.evidenceReason = $reason, r.resolver = $resolver,
                            r.sourceFilePath = $path, r.sourceLine = $line, r.sourceColumn = $column
                        """, new
                        {
                            source = edge.SourceId, target = edge.TargetId, properties = edge.Properties,
                            project = snapshot.ProjectContext, path = file.Path, callSite = edge.CallSite,
                            confidence = (object?)edge.Confidence, evidenceKind = edge.EvidenceKind?.ToString().ToLowerInvariant(),
                            reason = edge.EvidenceReason, resolver = edge.Resolver,
                            line = (object?)edge.SourceLine, column = (object?)edge.SourceColumn
                        });
                }
            }
            await RunSqlAsync(tx, """
                MATCH (n:CodeNode) WHERE n.sqlProject = $project AND n.sqlShared = true AND NOT (n)--()
                DELETE n
                """, new { project = snapshot.ProjectContext });
            cancellationToken.ThrowIfCancellationRequested();
        });
    }

    private static Task UpsertSqlNodeAsync(IAsyncQueryRunner tx, string project, SqlFileGraph file, CodeNode node) =>
        RunSqlAsync(tx, """
            MERGE (n:CodeNode {id: $id}) ON CREATE SET n.createdAt = $now
            SET n += $properties, n.name = $name, n.nameNormalized = toLower($name),
                n.type = CASE WHEN $type = 'DatabaseRelation' AND n.type IN ['DatabaseTable','DatabaseView'] THEN n.type ELSE $type END,
                n.namespace = $namespace, n.namespaceNormalized = toLower($namespace),
                n.projectContext = $project, n.projectContextNormalized = toLower($project),
                n.filePath = $file, n.filePathNormalized = toLower($file), n.fileRole = $role,
                n.lineNumber = $line, n.lineCount = $lineCount, n.sourceHash = $hash,
                n.summary = $summary, n.lastIndexedAt = $now, n.updatedAt = $now,
                n.sqlProject = $project, n.sqlFile = $file, n.sqlShared = $shared,
                n.analysisStatus = $status, n.analysisDiagnostic = $diagnostic
            """, new
            {
                id = node.Id, name = node.Name, type = node.Type.ToString(), @namespace = node.Namespace,
                project, file = node.FilePath, role = node.FileRole.ToString(), line = (object?)node.LineNumber,
                lineCount = (object?)node.LineCount, hash = node.SourceHash, summary = node.Summary,
                properties = node.Properties, shared = node.FilePath is null, status = file.Status,
                diagnostic = node.FilePath is null ? null : string.Join("; ", file.Diagnostics),
                now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });

    private static async Task RunSqlAsync(IAsyncQueryRunner tx, string cypher, object parameters) =>
        await (await tx.RunAsync(cypher, parameters)).ConsumeAsync();
}
