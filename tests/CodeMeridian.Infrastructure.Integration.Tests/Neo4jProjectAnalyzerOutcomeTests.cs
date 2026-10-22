using CodeMeridian.Core.Projects;
using CodeMeridian.Infrastructure.Graph;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Neo4j.Driver;

namespace CodeMeridian.Infrastructure.Integration.Tests;

[Collection(Neo4jCodeGraphRepositoryCollection.Name)]
public sealed class Neo4jProjectAnalyzerOutcomeTests
{
    [Fact]
    public async Task OutcomesAreGenerationScopedMergeConcurrentScopesAndKeepTerminalStatesImmutable()
    {
        var options = TestEnvironment.TryGetNeo4jOptions() ?? throw new InvalidOperationException("Neo4j integration settings are required.");
        var project = $"Integration.Outcomes.{Guid.NewGuid():N}";
        await using var repository = new Neo4jProjectProfileRepository(Options.Create(options));
        await repository.InitializeAsync();
        try
        {
            var generation = await repository.BeginAsync(project);
            await repository.PublishAsync(Inventory(project, generation));
            var running = new ProjectAnalyzerOutcomeSnapshot(project, generation, [Outcome(".", "running", "unknown")]);
            var mismatched = () => repository.PublishOutcomesAsync(running with { Outcomes = [running.Outcomes[0] with
                { Fingerprint = running.Outcomes[0].Fingerprint! with { Parser = "sha256:" + new string('b', 64) } }] });
            await mismatched.Should().ThrowAsync<ProjectProfileConflictException>();
            var missingFingerprint = () => repository.PublishOutcomesAsync(running with { Outcomes = [running.Outcomes[0] with { Fingerprint = null }] });
            await missingFingerprint.Should().ThrowAsync<ProjectProfileConflictException>();
            var mismatchedSource = () => repository.PublishOutcomesAsync(running with { Outcomes = [running.Outcomes[0] with
                { SourceFingerprint = "sha256:" + new string('b', 64) }] });
            await mismatchedSource.Should().ThrowAsync<ProjectProfileConflictException>();
            var missingSource = () => repository.PublishOutcomesAsync(running with { Outcomes = [running.Outcomes[0] with { SourceFingerprint = null }] });
            await missingSource.Should().ThrowAsync<ProjectProfileConflictException>();
            await repository.PublishOutcomesAsync(running);
            var completed = running with { Outcomes = [Outcome(".", "succeeded", "complete")] };
            await repository.PublishOutcomesAsync(completed);
            await repository.PublishOutcomesAsync(completed);
            var lateFailure = () => repository.PublishOutcomesAsync(running with { Outcomes = [Outcome(".", "failed", "unknown")] });
            await lateFailure.Should().ThrowAsync<ProjectProfileConflictException>();
            await Task.WhenAll(new[] { "docs/story", "docs/support" }.Select(scope => repository.PublishOutcomesAsync(
                new(project, generation, [Outcome(scope, "succeeded", "complete")]))));
            await using var restarted = new Neo4jProjectProfileRepository(Options.Create(options));
            (await restarted.GetAsync(project))!.AnalyzerOutcomes.Should().HaveCount(3);
            (await restarted.GetAsync(project))!.AnalyzerOutcomes.Should().OnlyContain(outcome => outcome.Fingerprint == Fingerprint());
            (await restarted.GetAsync(project))!.AnalyzerOutcomes.Should().OnlyContain(outcome => outcome.SourceFingerprint == Fingerprint().Implementation);
            var invalidScope = () => repository.PublishOutcomesAsync(new(project, generation, [Outcome("missing", "succeeded", "complete")]));
            await invalidScope.Should().ThrowAsync<ArgumentException>();
            var unsupported = () => repository.PublishOutcomesAsync(new(project, generation,
                [Outcome(".", "succeeded", "complete") with { Analyzer = "powershell" }]));
            await unsupported.Should().ThrowAsync<ArgumentException>();
            var next = await repository.BeginAsync(project);
            var stale = () => repository.PublishOutcomesAsync(completed);
            await stale.Should().ThrowAsync<ProjectProfileConflictException>();
            await repository.PublishAsync(Inventory(project, next));
            (await restarted.GetAsync(project))!.AnalyzerOutcomes.Should().BeEmpty();
            await stale.Should().ThrowAsync<ProjectProfileConflictException>();
        }
        finally
        {
            await using var driver = GraphDatabase.Driver(options.Uri, AuthTokens.Basic(options.Username, options.Password));
            await using var session = driver.AsyncSession();
            await (await session.RunAsync("MATCH (f:ProjectInventoryFile {project: $project}) DELETE f", new { project })).ConsumeAsync();
            await (await session.RunAsync("MATCH (s:ProjectProfileState {project: $project}) DELETE s", new { project })).ConsumeAsync();
        }
    }

    private static ProjectAnalyzerOutcome Outcome(string scope, string state, string coverage) =>
        new("documents", scope, state, coverage, "full", DateTimeOffset.UtcNow, "cli/test", [])
            { Fingerprint = Fingerprint(), SourceFingerprint = Fingerprint().Implementation };
    private static ProjectAnalyzerFingerprint Fingerprint() => new("sha256:" + new string('a', 64), "sha256:" + new string('a', 64));
    private static ProjectProfileSnapshot Inventory(string project, long generation) => new(generation,
        new(project, DateTimeOffset.UtcNow, "complete", 2, [new("markdown", 2, ["docs/story/chapter.md", "docs/support/README.md"])], [], 0,
            [new("documents", true, "enabled", "unknown", ["document_search"], []) { Fingerprint = Fingerprint() }, new("powershell", false, "unsupported", "unknown", [], [])], [], 0)
            { SourceFingerprint = Fingerprint().Implementation },
        [new("docs/story/chapter.md", "markdown"), new("docs/support/README.md", "markdown")]);
}
