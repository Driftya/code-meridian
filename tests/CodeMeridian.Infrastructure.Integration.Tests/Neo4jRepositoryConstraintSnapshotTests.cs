using CodeMeridian.Core.Projects;
using CodeMeridian.Core.RepositoryConstraints;
using CodeMeridian.Infrastructure.Graph;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Neo4j.Driver;

namespace CodeMeridian.Infrastructure.Integration.Tests;

[Collection(Neo4jCodeGraphRepositoryCollection.Name)]
public sealed class Neo4jRepositoryConstraintSnapshotTests
{
    [Fact]
    public async Task BoundPoliciesSurviveRestartReplayConflictAndInventoryReplacement()
    {
        var options = TestEnvironment.TryGetNeo4jOptions()
            ?? throw new InvalidOperationException("Neo4j integration connection details are required.");
        var project = $"Integration.Constraints.{Guid.NewGuid():N}";
        await using var repository = new Neo4jProjectProfileRepository(Options.Create(options));
        await repository.InitializeAsync();
        try
        {
            (await repository.GetRepositoryConstraintsAsync(project)).Should().BeNull();
            var first = await repository.BeginAsync(project);
            var hash = "sha256:" + new string('a', 64);
            var policy = new RepositoryConstraintPolicy("1.0", [new("documents", ["**/*.md"])],
                [new("location", "documents", ["docs/**"], [], new string('a', 200), "error", [])], []);
            var snapshot = new ProjectProfileSnapshot(first,
                new(project, DateTimeOffset.UtcNow, "complete", 2,
                    [new("configuration", 1, ["policy.json"]), new("markdown", 1, ["wrong.md"])], [], 0, [], [], 0)
                    { SourceFingerprint = hash, ConfigurationFingerprint = hash },
                [new("policy.json", "configuration"), new("wrong.md", "markdown")])
            {
                ConstraintEvidence = new("policy.json", hash, true, policy, ["excluded"])
            };
            await repository.PublishAsync(snapshot);
            await repository.PublishAsync(snapshot);
            await using var restarted = new Neo4jProjectProfileRepository(Options.Create(options));
            var stored = await restarted.GetRepositoryConstraintsAsync(project);
            stored!.Evidence.Should().BeEquivalentTo(snapshot.ConstraintEvidence);
            stored.Files.Should().BeEquivalentTo(snapshot.Files);
            stored.Generation.Should().Be(first);
            (await restarted.GetRepositoryConstraintsAsync(project + ".other")).Should().BeNull();
            await FluentActions.Awaiting(() => repository.PublishAsync(snapshot with
            {
                ConstraintEvidence = snapshot.ConstraintEvidence with { CaseSensitive = false }
            })).Should().ThrowAsync<ProjectProfileConflictException>();
            var next = await repository.BeginAsync(project);
            stored = await restarted.GetRepositoryConstraintsAsync(project);
            stored!.Generation.Should().Be(first);
            stored.LatestGeneration.Should().Be(next);
            await repository.PublishAsync(snapshot with { Generation = next, ConstraintEvidence = null });
            stored = await restarted.GetRepositoryConstraintsAsync(project);
            stored!.Generation.Should().Be(next);
            stored.Evidence.Should().BeNull();
            await FluentActions.Awaiting(() => repository.PublishAsync(snapshot with
            {
                ConstraintEvidence = snapshot.ConstraintEvidence with { PolicyFingerprint = "sha256:" + new string('b', 64) }
            })).Should().ThrowAsync<ProjectProfileConflictException>();
        }
        finally
        {
            await using var driver = GraphDatabase.Driver(options.Uri, AuthTokens.Basic(options.Username, options.Password));
            await using var session = driver.AsyncSession();
            await (await session.RunAsync("MATCH (f:ProjectInventoryFile {project: $project}) DELETE f", new { project })).ConsumeAsync();
            await (await session.RunAsync("MATCH (s:ProjectProfileState {project: $project}) DELETE s", new { project })).ConsumeAsync();
        }
    }
}
