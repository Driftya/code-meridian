using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Infrastructure.Configuration;
using CodeMeridian.Infrastructure.Graph;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CodeMeridian.Infrastructure.Integration.Tests;

[Collection(Neo4jCodeGraphRepositoryCollection.Name)]
public sealed class Neo4jSqlGraphIntegrationTests
{
    [Fact]
    public async Task Publication_ReplacesOwnedOccurrencesRetainsSharedObjectsAndPreservesFailedFiles()
    {
        var options = TestEnvironment.TryGetNeo4jOptions() ?? throw new InvalidOperationException("Neo4j integration configuration unavailable.");
        await using var repository = new Neo4jCodeGraphRepository(Options.Create(options), NullLogger<Neo4jCodeGraphRepository>.Instance);
        await repository.InitializeSqlGraphAsync();
        var project = $"Integration.Sql.{Guid.NewGuid():N}";
        var shared = new CodeNode { Id = project + "::Sql::Object", Name = "app.orders", Type = CodeNodeType.DatabaseTable, ProjectContext = project };
        var declaration = File("schema.sql");
        var reference = File("query.sql");
        try
        {
            var first = new SqlGraphSnapshot { ProjectContext = project, Files = [declaration, reference] };
            await repository.PublishSqlGraphAsync(first);
            (await repository.QueryEdgesAsync(shared.Id)).Count(e => e.Type == CodeEdgeType.Reads).Should().Be(4);
            await repository.PublishSqlGraphAsync(first); // Replay does not accumulate join/read occurrences.
            (await repository.QueryEdgesAsync(shared.Id)).Count(e => e.Type == CodeEdgeType.Reads).Should().Be(4);
            await repository.PublishSqlGraphAsync(first with { Files = [reference] });
            (await repository.QueryNodesAsync(new CodeGraphQuery { ProjectContext = project })).Should().Contain(n => n.Id == shared.Id);
            await repository.PublishSqlGraphAsync(first with { Files = [reference with { Status = "failed", Nodes = [reference.Nodes[0]], Edges = [], Diagnostics = ["syntax error"] }] });
            (await repository.QueryEdgesAsync(shared.Id)).Count(e => e.Type == CodeEdgeType.Reads).Should().Be(2);
            (await repository.FindImpactAsync(shared.Id)).Should().BeEmpty();
            await repository.PublishSqlGraphAsync(first with { Files = [reference] });
            (await repository.FindImpactAsync(shared.Id)).Should().Contain(n => n.Node.Id == reference.Nodes[0].Id);
            var manual = new CodeNode { Id = project + "::Manual", Name = "manual", Type = CodeNodeType.Class, ProjectContext = project };
            await repository.UpsertNodeAsync(manual);
            await repository.UpsertEdgeAsync(new CodeEdge { SourceId = manual.Id, TargetId = shared.Id, Type = CodeEdgeType.Uses });
            await repository.PublishSqlGraphAsync(first with { Files = [] });
            (await repository.QueryEdgesAsync(shared.Id)).Should().ContainSingle(e => e.Type == CodeEdgeType.Uses);
            (await repository.FindImpactAsync(shared.Id)).Should().Contain(n => n.Node.Id == manual.Id);
            (await repository.QueryNodesAsync(new CodeGraphQuery { ProjectContext = project })).Should().Contain(n => n.Id == shared.Id);
        }
        finally { await repository.DeleteProjectAsync(project); }

        SqlFileGraph File(string path)
        {
            var node = new CodeNode { Id = project + "::Sql::" + path, Name = path, Type = CodeNodeType.File, ProjectContext = project, FilePath = path };
            return new SqlFileGraph
            {
                Path = path, Status = "complete", Nodes = [node, shared],
                Edges = Enumerable.Range(1, 2).Select(line => new CodeEdge
                {
                    SourceId = node.Id, TargetId = shared.Id, Type = CodeEdgeType.Reads, SourceFilePath = path,
                    SourceLine = line, EvidenceKind = EdgeEvidenceKind.Extracted, Properties = new() { ["occurrence"] = path + line }
                }).ToArray()
            };
        }
    }
}
