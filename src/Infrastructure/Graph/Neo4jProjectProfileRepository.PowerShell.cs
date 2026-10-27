using System.Security.Cryptography;
using System.Text.Json;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Projects;
using Neo4j.Driver;

namespace CodeMeridian.Infrastructure.Graph;

public sealed partial class Neo4jProjectProfileRepository : IPowerShellGraphRepository
{
    public async Task PublishPowerShellGraphAsync(PowerShellGraphSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        if (snapshot.Validate() is { } error) throw new ArgumentException(error, nameof(snapshot));
        var fingerprint = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonOptions)));
        await using var session = _driver.AsyncSession();
        await session.ExecuteWriteAsync(async tx =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cursor = await tx.RunAsync("""
                MATCH (s:ProjectProfileState {project: $project})
                SET s.lockRevision = coalesce(s.lockRevision, 0) + 1
                RETURN s.generation AS generation, s.nextGeneration AS latest, s.profileJson AS profile,
                    s.powershellGeneration AS powershellGeneration, s.powershellFingerprint AS powershellFingerprint
                """, new { project = snapshot.Project });
            if (!await cursor.FetchAsync()) throw new ProjectProfileConflictException("Publish complete discovery before PowerShell facts.");
            var row = cursor.Current;
            if (row["generation"].As<long?>() != snapshot.Generation || row["latest"].As<long?>() != snapshot.Generation)
                throw new ProjectProfileConflictException("PowerShell facts require the latest complete inventory generation.");
            var profile = JsonSerializer.Deserialize<ProjectProfile>(row["profile"].As<string>(), JsonOptions)!;
            var previousGeneration = row["powershellGeneration"].As<long?>();
            var previousFingerprint = row["powershellFingerprint"].As<string?>();
            await cursor.ConsumeAsync();
            var inventoryRow = await (await tx.RunAsync("""
                MATCH (f:ProjectInventoryFile {project: $project, generation: $generation, kind: 'powershell'})
                WITH f ORDER BY f.path LIMIT 1001
                RETURN collect(f.path) AS paths
                """, new { project = snapshot.Project, generation = snapshot.Generation })).SingleAsync();
            var inventory = inventoryRow["paths"].As<List<string>>().Select(path => new ProjectInventoryFile(path, "powershell")).ToArray();
            if (snapshot.ValidateAgainst(profile, inventory) is { } bindingError)
                throw new ProjectProfileConflictException(bindingError);
            if (previousGeneration == snapshot.Generation)
            {
                if (previousFingerprint == fingerprint) return;
                throw new ProjectProfileConflictException("PowerShell publication is immutable within a generation; retry the exact snapshot or reserve a new generation.");
            }
            var ids = snapshot.Files.SelectMany(file => file.Nodes).Select(node => node.Id).ToArray();
            var collision = await (await tx.RunAsync("""
                UNWIND $ids AS id MATCH (n:CodeNode {id: id})
                WHERE n.powershellProject IS NULL OR n.powershellProject <> $project
                RETURN count(n) AS collisions
                """, new { project = snapshot.Project, ids })).SingleAsync();
            if (collision["collisions"].As<long>() != 0)
                throw new ProjectProfileConflictException("PowerShell identities collide with facts owned by another producer.");

            // Every successful consumer was rebound against this complete selection. Retained failed
            // consumers lose all prior bindings too, so they cannot point to changed/deleted definitions.
            await RunPowerShellAsync(tx, """
                MATCH ()-[r]->() WHERE r.powershellProject = $project DELETE r
                """, new { project = snapshot.Project });
            var failedPaths = snapshot.Files.Where(file => file.Status == "failed").Select(file => file.Path).ToArray();
            await RunPowerShellAsync(tx, """
                MATCH (n:CodeNode {powershellProject: $project})
                SET n.analysisStatus = CASE WHEN n.powershellFile IN $failedPaths THEN 'stale' ELSE 'removed' END
                WITH n WHERE NOT n.id IN $ids AND NOT n.powershellFile IN $failedPaths AND NOT (n)--()
                DELETE n
                """, new { project = snapshot.Project, failedPaths, ids });
            foreach (var file in snapshot.Files)
            foreach (var node in file.Nodes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await RunPowerShellAsync(tx, """
                    MERGE (n:CodeNode {id: $id}) ON CREATE SET n.createdAt = timestamp()
                    SET n += $properties, n.name = $name, n.nameNormalized = toLower($name), n.type = $type,
                        n.projectContext = $project, n.projectContextNormalized = toLower($project),
                        n.filePath = $path, n.filePathNormalized = toLower($path), n.fileRole = $role,
                        n.lineNumber = $line, n.lineCount = $lineCount, n.sourceHash = $sourceHash, n.sourceSnippet = $snippet,
                        n.powershellProject = $project, n.powershellFile = $path, n.powershellGeneration = $generation,
                        n.analysisStatus = $status, n.lastIndexedAt = timestamp(), n.updatedAt = timestamp()
                    """, new
                {
                    id = node.Id, name = node.Name, type = node.Type.ToString(), properties = node.Properties, project = snapshot.Project,
                    path = file.Path, role = node.FileRole.ToString(), line = (object?)node.LineNumber, lineCount = (object?)node.LineCount,
                    sourceHash = file.SourceFingerprint, snippet = node.SourceSnippet, generation = snapshot.Generation, status = file.Status
                });
            }
            foreach (var file in snapshot.Files)
            foreach (var edge in file.Edges)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await RunPowerShellAsync(tx, $$"""
                    MATCH (s:CodeNode {id: $source, powershellProject: $project}), (t:CodeNode {id: $target, powershellProject: $project})
                    CREATE (s)-[r:{{edge.Type}}]->(t)
                    SET r += $properties, r.powershellProject = $project, r.powershellFile = $path,
                        r.powershellGeneration = $generation, r.confidence = $confidence, r.evidenceKind = $kind,
                        r.evidenceReason = $reason, r.resolver = $resolver, r.sourceFilePath = $path,
                        r.sourceLine = $line, r.sourceColumn = $column, r.sourceEndLine = $endLine, r.sourceEndColumn = $endColumn
                    """, new
                {
                    source = edge.SourceId, target = edge.TargetId, project = snapshot.Project, path = file.Path,
                    properties = edge.Properties, generation = snapshot.Generation, confidence = (object?)edge.Confidence,
                    kind = edge.EvidenceKind?.ToString().ToLowerInvariant(), reason = edge.EvidenceReason, resolver = edge.Resolver,
                    line = (object?)edge.SourceLine, column = (object?)edge.SourceColumn,
                    endLine = (object?)edge.SourceEndLine, endColumn = (object?)edge.SourceEndColumn
                });
            }
            cancellationToken.ThrowIfCancellationRequested();
            await RunPowerShellAsync(tx, """
                MATCH (s:ProjectProfileState {project: $project})
                SET s.powershellGeneration = $generation, s.powershellFingerprint = $fingerprint, s.updatedAt = timestamp()
                """, new { project = snapshot.Project, generation = snapshot.Generation, fingerprint });
        });
    }

    private static async Task RunPowerShellAsync(IAsyncQueryRunner tx, string cypher, object parameters) =>
        await (await tx.RunAsync(cypher, parameters)).ConsumeAsync();
}
