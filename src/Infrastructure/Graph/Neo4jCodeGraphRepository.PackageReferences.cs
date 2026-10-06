using System.Text.Json;
using CodeMeridian.Core.CodeGraph;
using Neo4j.Driver;

namespace CodeMeridian.Infrastructure.Graph;

public sealed partial class Neo4jCodeGraphRepository
{
    private const string PackageResolver = "codemeridian.package";
    private static string PackageKey(string project, string id) => project.ToLowerInvariant() + "::" + id;
    private static string SymbolMatchKey(string assembly, string symbol) => assembly + "::" + symbol;

    private async Task InitializePackageReferencesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var session = _driver.AsyncSession();
        foreach (var label in new[] { "PackageManifest", "PackageExport", "PackageReference", "PackageRegistry", "PackagePublication" })
            await (await session.RunAsync($"CREATE CONSTRAINT {label.ToLowerInvariant()}_key IF NOT EXISTS FOR (n:{label}) REQUIRE n.key IS UNIQUE")).ConsumeAsync();
        foreach (var label in new[] { "PackageExport", "PackageReference" })
            await (await session.RunAsync($"CREATE INDEX {label.ToLowerInvariant()}_match IF NOT EXISTS FOR (n:{label}) ON (n.matchKey)")).ConsumeAsync();
        await (await session.RunAsync("CREATE INDEX packagereference_pending IF NOT EXISTS FOR (n:PackageReference) ON (n.pending)")).ConsumeAsync();
        // Use an implicit transaction so each batch commits before the next one starts.
        cancellationToken.ThrowIfCancellationRequested();
        await (await session.RunAsync($$"""
            MATCH ()-[r]->() WHERE r.packageReferenceKey IS NULL
            CALL {
                WITH r
                SET r.packageReferenceKey = ''
            } IN TRANSACTIONS OF {{InitializationBatchSize}} ROWS
            """)).ConsumeAsync();
    }

    public async Task BeginPackageReferenceIndexAsync(string projectContext, string generation, CancellationToken cancellationToken = default)
    {
        await using var session = _driver.AsyncSession();
        await session.ExecuteWriteAsync(async tx =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            await LockPackageRegistryAsync(tx, increment: false);
            await (await tx.RunAsync("""
                MERGE (n:PackagePublication {key: $key})
                SET n.generation = $generation, n.projectContextNormalized = $project, n.state = 'staging'
                """, new { key = PackageKey(projectContext, "publication"), generation, project = Normalize(projectContext) })).ConsumeAsync();
        });
    }

    public async Task PublishPackageReferencesAsync(PackageReferenceSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        await using var session = _driver.AsyncSession();
        await session.ExecuteWriteAsync(async tx =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            await LockPackageRegistryAsync(tx, increment: true);
            var project = Normalize(snapshot.ProjectContext);
            var publication = await (await tx.RunAsync("MATCH (n:PackagePublication {key: $key}) RETURN n.generation AS generation, n.state AS state",
                new { key = PackageKey(snapshot.ProjectContext, "publication") })).SingleAsync();
            if (publication["generation"].As<string>() != snapshot.Generation)
                throw new InvalidOperationException("A newer package-reference index run superseded this generation.");
            if (publication["state"].As<string>() == "completed") return;
            var sourceIds = snapshot.Exports.Select(export => export.SourceId).Concat(snapshot.References.Select(reference => reference.SourceId)).Distinct().ToArray();
            var ownership = await (await tx.RunAsync("""
                UNWIND $sourceIds AS id MATCH (n:CodeNode {id: id}) WHERE n.projectContextNormalized = $project
                RETURN count(n) AS count
                """, new { sourceIds, project })).SingleAsync();
            if (ownership["count"].As<long>() != sourceIds.Length)
                throw new InvalidOperationException("Package reference sources must exist in the publishing project's code graph.");
            var previous = await (await tx.RunAsync("""
                MATCH (n) WHERE (n:PackageExport OR n:PackageReference) AND n.projectContextNormalized = $project
                RETURN DISTINCT n.matchKey AS key
                """, new { project })).ToListAsync(record => record["key"].As<string>());
            var keys = previous.Concat(snapshot.Exports.Select(export => SymbolMatchKey(
                    snapshot.Scopes.Single(scope => scope.Id == export.ScopeId).AssemblyName, export.SymbolKey)))
                .Concat(snapshot.References.Select(reference => SymbolMatchKey(reference.AssemblyName, reference.SymbolKey)))
                .Distinct(StringComparer.Ordinal).ToArray();
            await (await tx.RunAsync("""
                MATCH ()-[r]->() WHERE r.resolver = $resolver AND r.consumerProject = $project DELETE r
                """, new { resolver = PackageResolver, project })).ConsumeAsync();
            await (await tx.RunAsync("""
                MATCH (n) WHERE (n:PackageExport OR n:PackageReference OR n:PackageManifest)
                  AND n.projectContextNormalized = $project DETACH DELETE n
                """, new { project })).ConsumeAsync();
            var manifest = new { key = PackageKey(snapshot.ProjectContext, "manifest"), projectContextNormalized = project,
                projectContext = snapshot.ProjectContext, snapshot.Generation, data = JsonSerializer.Serialize(snapshot) };
            await (await tx.RunAsync("""
                CREATE (n:PackageManifest) SET n = $manifest
                """, new { manifest = PackageRow(manifest) })).ConsumeAsync();
            var scopes = snapshot.Scopes.ToDictionary(scope => scope.Id);
            foreach (var batch in snapshot.Exports.Chunk(500))
            {
                var rows = batch.Select(export => new
                {
                    key = PackageKey(snapshot.ProjectContext, export.Id), projectContextNormalized = project,
                    projectContext = snapshot.ProjectContext, matchKey = SymbolMatchKey(scopes[export.ScopeId].AssemblyName, export.SymbolKey),
                    sourceId = export.SourceId, filePathNormalized = Normalize(export.FilePath),
                    data = JsonSerializer.Serialize(export), scope = JsonSerializer.Serialize(scopes[export.ScopeId])
                }).Select(PackageRow).ToArray();
                await (await tx.RunAsync("UNWIND $rows AS row CREATE (n:PackageExport) SET n = row", new { rows })).ConsumeAsync();
            }
            foreach (var batch in snapshot.References.Chunk(500))
            {
                var rows = batch.Select(reference => new
                {
                    key = PackageKey(snapshot.ProjectContext, reference.Id), projectContextNormalized = project,
                    projectContext = snapshot.ProjectContext, matchKey = SymbolMatchKey(reference.AssemblyName, reference.SymbolKey),
                    sourceId = reference.SourceId, filePathNormalized = Normalize(reference.FilePath), pending = true,
                    data = JsonSerializer.Serialize(reference), scope = JsonSerializer.Serialize(scopes[reference.ScopeId]),
                    resolution = JsonSerializer.Serialize(new PackageReferenceResolution("source_not_indexed", "Awaiting reconciliation."))
                }).Select(PackageRow).ToArray();
                await (await tx.RunAsync("UNWIND $rows AS row CREATE (n:PackageReference) SET n = row", new { rows })).ConsumeAsync();
            }
            await (await tx.RunAsync("""
                UNWIND $keys AS key MATCH (n:PackageReference {matchKey: key})
                SET n.pending = true
                WITH n OPTIONAL MATCH ()-[r]->() WHERE r.packageReferenceKey = n.key AND r.resolver = $resolver
                DELETE r
                """, new { keys, resolver = PackageResolver })).ConsumeAsync();
            await (await tx.RunAsync("MATCH (n:PackagePublication {key: $key}) SET n.state = 'completed'",
                new { key = PackageKey(snapshot.ProjectContext, "publication") })).ConsumeAsync();
        });
    }

    private static async Task<long> LockPackageRegistryAsync(IAsyncQueryRunner tx, bool increment)
    {
        var cursor = await tx.RunAsync("""
            MERGE (n:PackageRegistry {key: 'package-references'}) ON CREATE SET n.revision = 0
            SET n.revision = n.revision + $increment
            RETURN n.revision AS revision
            """, new { increment = increment ? 1 : 0 });
        return (await cursor.SingleAsync())["revision"].As<long>();
    }

    public async Task<IReadOnlyList<PendingPackageReference>> GetPendingPackageReferencesAsync(int limit, CancellationToken cancellationToken = default)
    {
        await using var session = _driver.AsyncSession();
        var cursor = await session.RunAsync("""
            MATCH (registry:PackageRegistry {key: 'package-references'})
            MATCH (n:PackageReference {pending: true})
            RETURN n, registry.revision AS revision ORDER BY n.key LIMIT $limit
            """, new { limit = Math.Clamp(limit, 1, 500) });
        var result = new List<PendingPackageReference>();
        await foreach (var record in cursor.WithCancellation(cancellationToken))
        {
            var node = record["n"].As<INode>();
            result.Add(new(ReadPackageJson<PackageSymbolReference>(node, "data"),
                ReadPackageJson<PackageBuildScope>(node, "scope"), node["projectContext"].As<string>(), record["revision"].As<long>()));
        }
        return result;
    }

    public async Task<IReadOnlyList<PackageExportCandidate>> GetPackageExportCandidatesAsync(PendingPackageReference reference, CancellationToken cancellationToken = default)
    {
        await using var session = _driver.AsyncSession();
        var cursor = await session.RunAsync("""
            MATCH (n:PackageExport {matchKey: $key})
            MATCH (:CodeNode {id: n.sourceId})
            RETURN n ORDER BY n.key LIMIT 1001
            """, new { key = SymbolMatchKey(reference.Reference.AssemblyName, reference.Reference.SymbolKey) });
        var result = new List<PackageExportCandidate>();
        await foreach (var record in cursor.WithCancellation(cancellationToken))
        {
            var node = record["n"].As<INode>();
            result.Add(new(ReadPackageJson<PackageSymbolExport>(node, "data"), ReadPackageJson<PackageBuildScope>(node, "scope"),
                node["projectContext"].As<string>()));
        }
        // A saturated result cannot establish unique ownership.
        return result.Count > 1000 ? result.Select(candidate => candidate with
            { Export = candidate.Export with { AmbiguousSource = true } }).ToArray() : result;
    }

    private static T ReadPackageJson<T>(INode node, string property) => JsonSerializer.Deserialize<T>(node[property].As<string>())!;

    private static Dictionary<string, object?> PackageRow<T>(T row)
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(row));
        return json.RootElement.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.ValueKind switch
        {
            JsonValueKind.String => (object?)property.Value.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => property.Value.GetInt64(),
            _ => null
        });
    }
}
