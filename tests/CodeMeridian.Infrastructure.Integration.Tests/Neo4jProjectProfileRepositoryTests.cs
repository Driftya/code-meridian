using CodeMeridian.Core.Projects;
using CodeMeridian.Infrastructure.Graph;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Neo4j.Driver;

namespace CodeMeridian.Infrastructure.Integration.Tests;

[Collection(Neo4jCodeGraphRepositoryCollection.Name)]
public sealed class Neo4jProjectProfileRepositoryTests
{
    [Fact]
    public async Task CompleteSnapshotsSurviveRestartReplaceDeletedFilesAndRejectLateUploads()
    {
        var options = TestEnvironment.TryGetNeo4jOptions()
            ?? throw new InvalidOperationException("Neo4j integration connection details are required.");
        var project = $"Integration.Profile.{Guid.NewGuid():N}";
        await using var repository = new Neo4jProjectProfileRepository(Options.Create(options));
        await repository.InitializeAsync();
        try
        {
            (await repository.GetAsync(project)).Should().BeNull();
            var first = await repository.BeginAsync(project);
            var snapshot = Snapshot(project, first, "old.md");
            await repository.PublishAsync(snapshot);
            await repository.PublishAsync(snapshot); // Network retry is idempotent.
            await using var restarted = new Neo4jProjectProfileRepository(Options.Create(options));
            var stored = await restarted.GetAsync(project, "old.md");
            stored!.Target!.Kind.Should().Be("markdown");
            stored.Profile.EvidenceSource.Should().Be("uploaded_discovery");
            stored.Profile.Analyzers.Should().OnlyContain(a => a.IndexingState == "unknown");
            (await restarted.GetAsync(project))!.Profile.FileCount.Should().Be(1);

            var generations = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => repository.BeginAsync(project)));
            generations.Should().OnlyHaveUniqueItems();
            var newest = generations.Max();
            var replacement = Snapshot(project, newest, "new.md");
            var files = new[] { new ProjectInventoryFile("new.md", "markdown") }
                .Concat(Enumerable.Range(0, 1000).Select(i => new ProjectInventoryFile($"docs/{i}.md", "markdown"))).ToArray();
            replacement = replacement with { Files = files, Profile = replacement.Profile with {
                FileCount = files.Length, FileKinds = [new("markdown", files.Length, ["new.md"])]
            } };
            await repository.PublishAsync(replacement);
            (await restarted.GetAsync(project, "docs/999.md"))!.Target.Should().NotBeNull();
            var lateUpload = () => repository.PublishAsync(Snapshot(project, generations.Min(), "late.md"));
            await lateUpload.Should().ThrowAsync<ProjectProfileConflictException>();
            (await restarted.GetAsync(project, "old.md"))!.Target.Should().BeNull();
            (await restarted.GetAsync(project, "new.md"))!.Target!.Path.Should().Be("new.md");
            (await restarted.GetAsync(project + ".other", "new.md")).Should().BeNull();

            var partialGeneration = await repository.BeginAsync(project);
            var partial = Snapshot(project, partialGeneration, "partial.md");
            var publishPartial = () => repository.PublishAsync(partial with { Profile = partial.Profile with { DiscoveryState = "partial" } });
            await publishPartial.Should().ThrowAsync<ArgumentException>();
            stored = await restarted.GetAsync(project, "new.md");
            stored!.Generation.Should().Be(newest);
            stored.LatestGeneration.Should().Be(partialGeneration);
            stored.Target.Should().NotBeNull();
            var differentRetry = () => repository.PublishAsync(Snapshot(project, newest, "changed.md"));
            await differentRetry.Should().ThrowAsync<ProjectProfileConflictException>();
        }
        finally
        {
            await using var driver = GraphDatabase.Driver(options.Uri, AuthTokens.Basic(options.Username, options.Password));
            await using var session = driver.AsyncSession();
            await (await session.RunAsync("MATCH (f:ProjectInventoryFile {project: $project}) DELETE f", new { project })).ConsumeAsync();
            await (await session.RunAsync("MATCH (s:ProjectProfileState {project: $project}) DELETE s", new { project })).ConsumeAsync();
        }
    }

    private static ProjectProfileSnapshot Snapshot(string project, long generation, string path) => new(generation,
        new(project, DateTimeOffset.UtcNow, "complete", 1, [new("markdown", 1, [path])],
            [new("markdown", ".", null)], 1, [new("documents", true, "enabled", "unknown", ["document_search"], [])], [], 0),
        [new(path, "markdown")]);
}
