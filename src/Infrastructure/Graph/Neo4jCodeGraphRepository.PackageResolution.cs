using System.Text.Json;
using CodeMeridian.Core.CodeGraph;
using Neo4j.Driver;

namespace CodeMeridian.Infrastructure.Graph;

public sealed partial class Neo4jCodeGraphRepository
{
    public async Task<bool> TryResolvePackageReferenceAsync(PendingPackageReference pending, PackageReferenceResolution resolution, CancellationToken cancellationToken = default)
    {
        await using var session = _driver.AsyncSession();
        return await session.ExecuteWriteAsync(async tx =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await LockPackageRegistryAsync(tx, increment: false) != pending.Revision) return false;
            var key = PackageKey(pending.ProjectContext, pending.Reference.Id);
            var exists = await (await tx.RunAsync("MATCH (n:PackageReference {key: $key, pending: true}) RETURN count(n) AS count", new { key })).SingleAsync();
            if (exists["count"].As<long>() == 0) return true;
            await (await tx.RunAsync("MATCH ()-[r]->() WHERE r.packageReferenceKey = $key AND r.resolver = $resolver DELETE r",
                new { key, resolver = PackageResolver })).ConsumeAsync();
            await (await tx.RunAsync("""
                MATCH (n:PackageReference {key: $key})
                SET n.resolution = $data, n.targetId = $targetId, n.pending = false, n.status = $status
                """, new { key, data = JsonSerializer.Serialize(resolution), targetId = resolution.TargetId, status = resolution.Status })).ConsumeAsync();
            if (resolution.Status == "verified_source" && resolution.TargetId is not null)
            {
                // This key distinguishes derived edges from manually ingested/local relationships with the same endpoints.
                var type = pending.Reference.Relationship switch
                {
                    "Calls" => "Calls", "Uses" => "Uses", "Inherits" => "Inherits", "Implements" => "Implements",
                    _ => throw new ArgumentException("Unsupported package relationship.")
                };
                await (await tx.RunAsync($$"""
                    MATCH (source:CodeNode {id: $sourceId}), (target:CodeNode {id: $targetId})
                    MERGE (source)-[r:{{type}} {packageReferenceKey: $key}]->(target)
                    ON CREATE SET r.createdAt = datetime()
                    SET r.updatedAt = datetime(), r.resolver = $resolver, r.consumerProject = $project,
                        r.evidenceKind = 'extracted', r.evidenceReason = 'verified_package_source', r.confidence = 1.0,
                        r.sourceFilePath = $file, r.sourceLine = $line, r.sourceColumn = $column,
                        r.sourceEndLine = $endLine, r.sourceEndColumn = $endColumn
                    SET r.sourceId = $sourceId, r.targetId = $targetId, r.packageRevision = $revision,
                        r.sourceScopeId = $scopeId
                    """, new { sourceId = pending.Reference.SourceId, targetId = resolution.TargetId, key,
                    resolver = PackageResolver, project = Normalize(pending.ProjectContext), file = pending.Reference.FilePath,
                    line = pending.Reference.Line, column = pending.Reference.Column, endLine = pending.Reference.EndLine, endColumn = pending.Reference.EndColumn,
                    revision = pending.Revision, scopeId = pending.Reference.ScopeId })).ConsumeAsync();
            }
            return true;
        });
    }

    public async Task<IReadOnlyList<PackageDependency>> GetPackageDependenciesAsync(string? projectContext = null, string? targetId = null, CancellationToken cancellationToken = default)
    {
        await using var session = _driver.AsyncSession();
        var cursor = await session.RunAsync("""
            MATCH (n:PackageReference)
            OPTIONAL MATCH (source:CodeNode {id: n.sourceId})
            OPTIONAL MATCH (target:CodeNode {id: n.targetId})
            WITH n, source, target
            WHERE ($project IS NULL OR n.projectContextNormalized = $project OR target.projectContextNormalized = $project)
              AND ($targetId IS NULL OR target.id = $targetId
                OR EXISTS { MATCH (:CodeNode {id: $targetId})-[:Contains*1..3]->(target) })
            RETURN n, source, target ORDER BY n.key LIMIT 100
            """, new { project = Normalize(projectContext), targetId });
        var result = new List<PackageDependency>();
        await foreach (var record in cursor.WithCancellation(cancellationToken))
        {
            var node = record["n"].As<INode>();
            var resolution = ReadPackageJson<PackageReferenceResolution>(node, "resolution");
            if (node["pending"].As<bool>()) resolution = new("pending", "Source association is awaiting reconciliation.");
            result.Add(new(node["projectContext"].As<string>(), ReadPackageJson<PackageSymbolReference>(node, "data"),
                ReadPackageJson<PackageBuildScope>(node, "scope"), resolution,
                record["source"] is INode source ? MapToCodeNode(source) : null,
                record["target"] is INode target ? MapToCodeNode(target) : null));
        }
        return result;
    }

    private async Task InvalidatePackageReferencesAsync(string projectContext, string? filePath, CancellationToken cancellationToken)
    {
        await using var session = _driver.AsyncSession();
        await session.ExecuteWriteAsync(async tx =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            await LockPackageRegistryAsync(tx, increment: true);
            await InvalidatePackageReferencesAsync(tx, projectContext, filePath);
        });
    }

    private async Task InvalidateChangedPackageFileAsync(IAsyncQueryRunner tx, CodeNode node, CancellationToken cancellationToken)
    {
        if (node.Type != CodeNodeType.File || node.ProjectContext is null || node.FilePath is null) return;
        cancellationToken.ThrowIfCancellationRequested();
        var changed = await (await tx.RunAsync("""
            MATCH (file:CodeNode {id: $id})
            WHERE $hash IS NULL OR file.sourceHash IS NULL OR file.sourceHash <> $hash
            MATCH (n) WHERE (n:PackageExport OR n:PackageReference)
              AND n.projectContextNormalized = $project AND n.filePathNormalized = $file
            RETURN count(n) AS count
            """, new { id = node.Id, hash = node.SourceHash, project = Normalize(node.ProjectContext), file = Normalize(node.FilePath) })).SingleAsync();
        if (changed["count"].As<long>() == 0) return;
        await LockPackageRegistryAsync(tx, increment: true);
        await InvalidatePackageReferencesAsync(tx, node.ProjectContext, node.FilePath);
    }

    private static async Task InvalidatePackageReferencesAsync(IAsyncQueryRunner tx, string projectContext, string? filePath)
    {
        var project = Normalize(projectContext);
        var file = Normalize(filePath);
        await (await tx.RunAsync("""
            MATCH ()-[r]->() WHERE r.resolver = $resolver AND r.consumerProject = $project
              AND ($file IS NULL OR toLower(r.sourceFilePath) = $file) DELETE r
            """, new { resolver = PackageResolver, project, file })).ConsumeAsync();
        var keys = await (await tx.RunAsync("""
            MATCH (n) WHERE (n:PackageExport OR n:PackageReference) AND n.projectContextNormalized = $project
              AND ($file IS NULL OR n.filePathNormalized = $file)
            RETURN DISTINCT n.matchKey AS key
            """, new { project, file })).ToListAsync(record => record["key"].As<string>());
        await (await tx.RunAsync("""
            MATCH (n) WHERE (n:PackageExport OR n:PackageReference OR n:PackageManifest OR n:PackagePublication)
              AND n.projectContextNormalized = $project AND ($file IS NULL OR n.filePathNormalized = $file)
            DETACH DELETE n
            """, new { project, file })).ConsumeAsync();
        await (await tx.RunAsync("""
            UNWIND $keys AS key MATCH (n:PackageReference {matchKey: key})
            SET n.pending = true, n.targetId = null, n.resolution = $resolution
            WITH n OPTIONAL MATCH ()-[r]->() WHERE r.packageReferenceKey = n.key AND r.resolver = $resolver
            DELETE r
            """, new { keys, resolver = PackageResolver,
            resolution = JsonSerializer.Serialize(new PackageReferenceResolution("source_not_indexed", "Indexed source changed or was removed.")) })).ConsumeAsync();
    }
}
