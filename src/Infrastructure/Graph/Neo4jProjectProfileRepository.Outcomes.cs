using System.Text.Json;
using CodeMeridian.Core.Projects;
using Neo4j.Driver;

namespace CodeMeridian.Infrastructure.Graph;

public sealed partial class Neo4jProjectProfileRepository
{
    public async Task PublishOutcomesAsync(ProjectAnalyzerOutcomeSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        if (snapshot.Validate() is { } error) throw new ArgumentException(error, nameof(snapshot));
        await using var session = _driver.AsyncSession();
        await session.ExecuteWriteAsync(async tx =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cursor = await tx.RunAsync("""
                MATCH (s:ProjectProfileState {project: $project})
                SET s.lockRevision = coalesce(s.lockRevision, 0) + 1
                RETURN s.generation AS generation, s.nextGeneration AS latest,
                    s.profileJson AS profile, coalesce(s.outcomesJson, '[]') AS outcomes
                """, new { project = snapshot.Project });
            if (!await cursor.FetchAsync()) throw new ProjectProfileConflictException("Publish complete discovery before analyzer outcomes.");
            var record = cursor.Current;
            if (record["generation"].As<long?>() != snapshot.Generation || record["latest"].As<long>() != snapshot.Generation)
                throw new ProjectProfileConflictException("Analyzer outcomes must match the latest complete inventory generation.");
            var profile = JsonSerializer.Deserialize<ProjectProfile>(record["profile"].As<string>(), JsonOptions)!;
            var outcomes = (JsonSerializer.Deserialize<ProjectAnalyzerOutcome[]>(record["outcomes"].As<string>(), JsonOptions) ?? [])
                .ToDictionary(outcome => (outcome.Analyzer, outcome.Scope));
            await cursor.ConsumeAsync();
            foreach (var outcome in snapshot.Outcomes)
            {
                var analyzer = profile.Analyzers.SingleOrDefault(analyzer => analyzer.Id == outcome.Analyzer);
                if (analyzer is not { Supported: true, ConfigurationState: "enabled" })
                    throw new ArgumentException("Outcomes require an enabled supported analyzer.", nameof(snapshot));
                if (outcome.Fingerprint != analyzer.Fingerprint)
                    throw new ProjectProfileConflictException("Analyzer fingerprints must match the published discovery generation.");
                if (outcome.SourceFingerprint != profile.SourceFingerprint)
                    throw new ProjectProfileConflictException("Outcome source fingerprints must match the published discovery generation.");
                if (outcome.Scope != ".")
                {
                    var scopeRecord = await (await tx.RunAsync("""
                        MATCH (f:ProjectInventoryFile {project: $project, generation: $generation})
                        WHERE f.path STARTS WITH $prefix RETURN count(f) AS files
                        """, new { project = snapshot.Project, generation = snapshot.Generation, prefix = outcome.Scope + "/" })).SingleAsync();
                    if (scopeRecord["files"].As<long>() == 0)
                        throw new ArgumentException("Outcome scope must contain files in the matching inventory.", nameof(snapshot));
                }
                var key = (outcome.Analyzer, outcome.Scope);
                if (outcomes.TryGetValue(key, out var previous) && previous.State != "running"
                    && JsonSerializer.Serialize(previous, JsonOptions) != JsonSerializer.Serialize(outcome, JsonOptions))
                    throw new ProjectProfileConflictException("Terminal outcomes are immutable within a generation.");
                outcomes[key] = outcome;
            }
            if (outcomes.Count > 128) throw new ArgumentException("A generation supports at most 128 analyzer scopes.", nameof(snapshot));
            cancellationToken.ThrowIfCancellationRequested();
            var outcomesJson = JsonSerializer.Serialize(outcomes.Values.OrderBy(o => o.Analyzer, StringComparer.Ordinal)
                .ThenBy(o => o.Scope, StringComparer.Ordinal), JsonOptions);
            await (await tx.RunAsync("""
                MATCH (s:ProjectProfileState {project: $project})
                SET s.outcomesJson = $outcomesJson, s.updatedAt = timestamp()
                """, new { project = snapshot.Project, outcomesJson })).ConsumeAsync();
        });
    }
}
