using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Projects;
using CodeMeridian.Infrastructure.Graph;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Neo4j.Driver;

namespace CodeMeridian.Infrastructure.Integration.Tests;

[Collection(Neo4jCodeGraphRepositoryCollection.Name)]
public sealed class Neo4jSqlProducerEvidenceTests
{
    [Fact]
    public async Task BoundPublicationRejectsLateAndLegacyWritesAndPreservesOwnershipThroughRecovery()
    {
        var options = TestEnvironment.TryGetNeo4jOptions() ?? throw new InvalidOperationException("Isolated Neo4j settings are required.");
        var project = "Integration.SqlProducer." + Guid.NewGuid().ToString("N");
        var hash = "sha256:" + new string('a', 64);
        var worker = new ProjectAnalyzerFingerprint(hash, hash);
        await using var profiles = new Neo4jProjectProfileRepository(Options.Create(options));
        await profiles.InitializeAsync();
        await using var graph = new Neo4jCodeGraphRepository(Options.Create(options), NullLogger<Neo4jCodeGraphRepository>.Instance);
        await graph.InitializeAsync();
        await using var driver = GraphDatabase.Driver(options.Uri, AuthTokens.Basic(options.Username, options.Password));
        await using var session = driver.AsyncSession();
        try
        {
            var generation = await profiles.BeginAsync(project);
            await profiles.PublishAsync(Profile(generation));
            var snapshot = Snapshot(generation);
            await Task.WhenAll(graph.PublishSqlGraphAsync(snapshot), graph.PublishSqlGraphAsync(snapshot));
            (await Count("MATCH ()-[r {sqlProject: $project}]->() RETURN count(r) AS count")).Should().Be(1);
            await FluentActions.Awaiting(() => graph.PublishSqlGraphAsync(snapshot with { Files = [] }))
                .Should().ThrowAsync<ProjectProfileConflictException>();
            await FluentActions.Awaiting(() => graph.PublishSqlGraphAsync(snapshot with { SourceFingerprint = "sha256:" + new string('b', 64) }))
                .Should().ThrowAsync<ProjectProfileConflictException>();
            await FluentActions.Awaiting(() => graph.PublishSqlGraphAsync(snapshot with { Generation = null, SourceFingerprint = null, Fingerprint = null }))
                .Should().ThrowAsync<ProjectProfileConflictException>();
            await (await session.RunAsync("""
                MATCH (target:CodeNode {id: $target})
                CREATE (manual:CodeNode {id: $manual, projectContext: $project, type: 'ExternalConcept', name: 'Manual'})
                CREATE (manual)-[:Uses {manual: true}]->(target)
                """, new { project, target = project + "::Sql::Object::table", manual = project + "::Manual" })).ConsumeAsync();

            generation = await profiles.BeginAsync(project);
            await FluentActions.Awaiting(() => graph.PublishSqlGraphAsync(snapshot)).Should().ThrowAsync<ProjectProfileConflictException>();
            await profiles.PublishAsync(Profile(generation));
            var failed = Snapshot(generation);
            failed = failed with { Files = [failed.Files[0] with { Status = "failed", Nodes = [failed.Files[0].Nodes[0]], Edges = [] }] };
            await graph.PublishSqlGraphAsync(failed);
            (await Count("MATCH ()-[r {sqlProject: $project, sqlStale: true}]->() RETURN count(r) AS count")).Should().Be(1);

            generation = await profiles.BeginAsync(project);
            await profiles.PublishAsync(Profile(generation));
            await graph.PublishSqlGraphAsync(Snapshot(generation));
            (await Count("MATCH ()-[r {sqlProject: $project, sqlStale: false}]->() RETURN count(r) AS count")).Should().Be(1);

            generation = await profiles.BeginAsync(project);
            var empty = Profile(generation);
            await profiles.PublishAsync(empty with { Files = [], Profile = empty.Profile with { FileCount = 0, FileKinds = [] } });
            await graph.PublishSqlGraphAsync(Snapshot(generation) with { Files = [] });
            (await Count("MATCH ()-[r {sqlProject: $project}]->() RETURN count(r) AS count")).Should().Be(0);
            (await Count("MATCH (n:CodeNode {projectContext: $project})-[r {manual: true}]->() RETURN count(r) AS count")).Should().Be(1);
        }
        finally
        {
            await (await session.RunAsync("MATCH (n:CodeNode {projectContext: $project}) DETACH DELETE n", new { project })).ConsumeAsync();
            await (await session.RunAsync("MATCH (n:ProjectInventoryFile {project: $project}) DELETE n", new { project })).ConsumeAsync();
            await (await session.RunAsync("MATCH (n:ProjectProfileState {project: $project}) DELETE n", new { project })).ConsumeAsync();
            await (await session.RunAsync("MATCH (n:SqlIndexState {projectContext: $project}) DELETE n", new { project })).ConsumeAsync();
        }

        async Task<long> Count(string query) => ValueExtensions.As<long>((await (await session.RunAsync(query, new { project })).SingleAsync())["count"]);
        ProjectProfileSnapshot Profile(long generation) => new(generation, new(project, DateTimeOffset.UtcNow, "complete", 1,
            [new("sql", 1, ["a.sql"])], [], 0,
            [new("sql", true, "enabled", "unknown", ["sql_object_dependencies"], []) { Fingerprint = worker }], [], 0)
            { SourceFingerprint = hash }, [new("a.sql", "sql")]);
        SqlGraphSnapshot Snapshot(long generation)
        {
            var file = new CodeNode { Id = project + "::Sql::File::a", Name = "a.sql", Type = CodeNodeType.File, FilePath = "a.sql", ProjectContext = project };
            var table = new CodeNode { Id = project + "::Sql::Object::table", Name = "app.t", Type = CodeNodeType.DatabaseTable, ProjectContext = project };
            return new() { ProjectContext = project, Generation = generation, SourceFingerprint = hash, Fingerprint = worker,
                Files = [new() { Path = "a.sql", Status = "complete", SourceFingerprint = hash, Nodes = [file, table],
                    Edges = [new() { SourceId = file.Id, TargetId = table.Id, Type = CodeEdgeType.Declares, SourceFilePath = "a.sql" }] }] };
        }
    }
}
