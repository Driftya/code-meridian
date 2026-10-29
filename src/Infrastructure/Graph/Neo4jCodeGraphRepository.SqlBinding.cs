using System.Security.Cryptography;
using System.Text.Json;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Projects;
using Neo4j.Driver;

namespace CodeMeridian.Infrastructure.Graph;

public sealed partial class Neo4jCodeGraphRepository
{
    private static async Task<(string? Fingerprint, bool Replay)> ValidateSqlBindingAsync(IAsyncQueryRunner tx,
        SqlGraphSnapshot snapshot, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var cursor = await tx.RunAsync("""
            MATCH (s:ProjectProfileState {project: $project})
            SET s.lockRevision = coalesce(s.lockRevision, 0) + 1
            RETURN s.generation AS generation, s.nextGeneration AS latest, s.profileJson AS profile,
                s.sqlGeneration AS sqlGeneration, s.sqlFingerprint AS sqlFingerprint
            """, new { project = snapshot.ProjectContext });
        var exists = await cursor.FetchAsync();
        if (snapshot.Generation is null)
        {
            if (exists && cursor.Current["sqlGeneration"].As<long?>() is not null)
                throw new ProjectProfileConflictException("This project requires generation-bound SQL publication; legacy writes cannot replace audited facts.");
            await cursor.ConsumeAsync();
            return (null, false);
        }
        if (!exists || cursor.Current["generation"].As<long?>() != snapshot.Generation
            || cursor.Current["latest"].As<long?>() != snapshot.Generation)
            throw new ProjectProfileConflictException("SQL publication requires the latest complete project inventory.");
        var row = cursor.Current;
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var profile = JsonSerializer.Deserialize<ProjectProfile>(row["profile"].As<string>(), options)!;
        var analyzer = profile.Analyzers.SingleOrDefault(item => item.Id == "sql");
        if (profile.Project != snapshot.ProjectContext || profile.SourceFingerprint != snapshot.SourceFingerprint
            || analyzer is not { Supported: true, ConfigurationState: "enabled" } || analyzer.Fingerprint != snapshot.Fingerprint)
            throw new ProjectProfileConflictException("SQL source and worker identities must match current discovery.");
        var previous = row["sqlGeneration"].As<long?>();
        var previousHash = row["sqlFingerprint"].As<string?>();
        await cursor.ConsumeAsync();
        var inventory = await (await tx.RunAsync("""
            MATCH (f:ProjectInventoryFile {project: $project, generation: $generation, kind: 'sql'})
            WITH f ORDER BY f.path LIMIT 10001 RETURN collect(f.path) AS paths
            """, new { project = snapshot.ProjectContext, generation = snapshot.Generation })).SingleAsync();
        if (!inventory["paths"].As<List<string>>().SequenceEqual(snapshot.Files.Select(file => file.Path).Order(StringComparer.Ordinal), StringComparer.Ordinal))
            throw new ProjectProfileConflictException("SQL publication must cover the complete accepted SQL inventory.");
        var fingerprint = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(snapshot, options)));
        if (previous == snapshot.Generation)
        {
            if (previousHash == fingerprint) return (fingerprint, true);
            throw new ProjectProfileConflictException("SQL publication is immutable within a generation; retry the exact snapshot.");
        }
        return (fingerprint, false);
    }
}
