using System.Text.Json;
using CodeMeridian.Core.Projects;
using CodeMeridian.Core.RepositoryConstraints;
using Neo4j.Driver;

namespace CodeMeridian.Infrastructure.Graph;

public sealed partial class Neo4jProjectProfileRepository
{
    public async Task<StoredRepositoryConstraintSnapshot?> GetRepositoryConstraintsAsync(
        string project, CancellationToken cancellationToken = default)
    {
        ValidateProject(project);
        cancellationToken.ThrowIfCancellationRequested();
        await using var session = _driver.AsyncSession();
        return await session.ExecuteReadAsync(async tx =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cursor = await tx.RunAsync("""
                MATCH (s:ProjectProfileState {project: $project})
                WHERE s.profileJson IS NOT NULL
                CALL {
                    WITH s
                    MATCH (f:ProjectInventoryFile {project: $project, generation: s.generation})
                    WITH f ORDER BY f.path LIMIT 100001
                    RETURN collect({path: f.path, kind: f.kind}) AS files
                }
                RETURN s.generation AS generation, s.nextGeneration AS latest,
                    s.profileJson AS profile, s.constraintJson AS constraints, s.markdownJson AS markdown, files
                """, new { project });
            if (!await cursor.FetchAsync()) return null;
            cancellationToken.ThrowIfCancellationRequested();
            var row = cursor.Current;
            var files = row["files"].As<List<IDictionary<string, object>>>();
            if (files.Count > 100000) throw new InvalidOperationException("Stored inventory exceeds its publication limit.");
            var profile = JsonSerializer.Deserialize<ProjectProfile>(row["profile"].As<string>(), JsonOptions)
                ?? throw new InvalidOperationException("Stored project profile is invalid.");
            var json = row["constraints"].As<string?>();
            var result = new StoredRepositoryConstraintSnapshot(row["generation"].As<long>(), row["latest"].As<long>(),
                profile, files.Select(file => new ProjectInventoryFile(file["path"].As<string>(), file["kind"].As<string>())).ToArray(),
                json is null ? null : JsonSerializer.Deserialize<RepositoryConstraintEvidence>(json, JsonOptions))
            {
                MarkdownEvidence = row["markdown"].As<string?>() is { } markdown
                    ? JsonSerializer.Deserialize<MarkdownReferenceEvidence>(markdown, JsonOptions) : null
            };
            if (result.MarkdownEvidence?.Validate(new(result.Generation, result.Profile, result.Files)) is { } error)
                throw new InvalidOperationException(error);
            return result;
        });
    }
}
