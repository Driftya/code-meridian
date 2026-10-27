using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Projects;
using CodeMeridian.Infrastructure.Graph;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Neo4j.Driver;

namespace CodeMeridian.Infrastructure.Integration.Tests;

[Collection(Neo4jCodeGraphRepositoryCollection.Name)]
public sealed class Neo4jPowerShellPublicationTests
{
    [Fact]
    public async Task OwnedReplacementReplayFailureAndDeletionPreserveManualFacts()
    {
        var options = TestEnvironment.TryGetNeo4jOptions() ?? throw new InvalidOperationException("An isolated Neo4j acceptance connection is required.");
        var project = "Integration.PowerShell." + Guid.NewGuid().ToString("N");
        var hash = "sha256:" + new string('a', 64);
        var worker = new ProjectAnalyzerFingerprint(hash, hash);
        await using var profiles = new Neo4jProjectProfileRepository(Options.Create(options));
        await profiles.InitializeAsync();
        await using var driver = GraphDatabase.Driver(options.Uri, AuthTokens.Basic(options.Username, options.Password));
        await using var session = driver.AsyncSession();
        try
        {
            var generation = await profiles.BeginAsync(project);
            await profiles.PublishAsync(Profile(generation));
            var graph = Graph(generation);
            await Task.WhenAll(profiles.PublishPowerShellGraphAsync(graph), profiles.PublishPowerShellGraphAsync(graph));
            (await Count("MATCH ()-[r {powershellProject: $project}]->() RETURN count(r) AS count")).Should().Be(1);
            await (await session.RunAsync("""
                MATCH (target:CodeNode {id: $target})
                CREATE (manual:CodeNode {id: $manual, projectContext: $project, type: 'ExternalConcept', name: 'Manual'})
                CREATE (manual)-[:Uses {manual: true}]->(target)
                """, new { target = project + "::PowerShellBody::work", manual = project + "::Manual", project })).ConsumeAsync();
            var conflict = () => profiles.PublishPowerShellGraphAsync(graph with { Files = [] });
            await conflict.Should().ThrowAsync<ProjectProfileConflictException>();
            (await Count("MATCH ()-[r {powershellProject: $project}]->() RETURN count(r) AS count")).Should().Be(1);

            generation = await profiles.BeginAsync(project);
            await profiles.PublishAsync(Profile(generation));
            await profiles.PublishPowerShellGraphAsync(Graph(generation) with { Files = [new("a.ps1", "failed", null, [], [])] });
            (await Count("MATCH ()-[r {powershellProject: $project}]->() RETURN count(r) AS count")).Should().Be(0);
            var stale = await (await session.RunAsync("MATCH (n:CodeNode {id: $id}) RETURN n.analysisStatus AS status", new { id = project + "::PowerShellBody::work" })).SingleAsync();
            ValueExtensions.As<string>(stale["status"]).Should().Be("stale");
            var late = () => profiles.PublishPowerShellGraphAsync(graph);
            await late.Should().ThrowAsync<ProjectProfileConflictException>();

            generation = await profiles.BeginAsync(project);
            await profiles.PublishAsync(Profile(generation) with { Files = [], Profile = Profile(generation).Profile with { FileCount = 0, FileKinds = [] } });
            await profiles.PublishPowerShellGraphAsync(new(project, generation, hash, worker, []));
            (await Count("MATCH (n:CodeNode {powershellProject: $project}) RETURN count(n) AS count")).Should().Be(1, "the manually referenced removed node survives");
            (await Count("MATCH (n:CodeNode {projectContext: $project})-[r {manual: true}]->() RETURN count(r) AS count")).Should().Be(1);
        }
        finally
        {
            await (await session.RunAsync("MATCH (n:CodeNode {projectContext: $project}) DETACH DELETE n", new { project })).ConsumeAsync();
            await (await session.RunAsync("MATCH (n:ProjectInventoryFile {project: $project}) DELETE n", new { project })).ConsumeAsync();
            await (await session.RunAsync("MATCH (n:ProjectProfileState {project: $project}) DELETE n", new { project })).ConsumeAsync();
        }

        async Task<long> Count(string query) => ValueExtensions.As<long>((await (await session.RunAsync(query, new { project })).SingleAsync())["count"]);

        ProjectProfileSnapshot Profile(long generation) => new(generation,
            new(project, DateTimeOffset.UtcNow, "complete", 1, [new("powershell", 1, ["a.ps1"])], [], 0,
                [new("powershell", true, "enabled", "unknown", [], []) { Fingerprint = worker }], [], 0) { SourceFingerprint = hash }, [new("a.ps1", "powershell")]);

        PowerShellGraphSnapshot Graph(long generation)
        {
            var file = new CodeNode { Id = project + "::PowerShellFile::a", Name = "a.ps1", Type = CodeNodeType.File,
                ProjectContext = project, FilePath = "a.ps1", SourceHash = hash };
            var method = file with { Id = project + "::PowerShellBody::work", Type = CodeNodeType.Method, Name = "Work", SourceSnippet = "function Work {}" };
            return new(project, generation, hash, worker, [new("a.ps1", "complete", hash, [file, method],
                [new() { SourceId = file.Id, TargetId = method.Id, Type = CodeEdgeType.Contains, SourceFilePath = "a.ps1" }])]);
        }
    }
}
