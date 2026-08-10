using CodeMeridian.Core.Knowledge;
using CodeMeridian.Infrastructure.Graph;
using CodeMeridian.Infrastructure.Knowledge;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Neo4j.Driver;

namespace CodeMeridian.Infrastructure.Integration.Tests;

[Collection(Neo4jCodeGraphRepositoryCollection.Name)]
public sealed class Neo4jChangeContextRepositoryIntegrationTests : Neo4jCodeGraphRepositoryIntegrationTestBase
{
    [Fact]
    public async Task UpsertAsync_IsIdempotent_PreservesOrphans_AndRelinksReindexedTargets()
    {
        await using var contextRepository = new Neo4jChangeContextRepository(Options.Create(_options));
        await using var vectorRepository = new Neo4jVectorRepository(
            Options.Create(_options),
            NullLogger<Neo4jVectorRepository>.Instance);
        await using var driver = GraphDatabase.Driver(
            _options.Uri,
            AuthTokens.Basic(_options.Username, _options.Password));
        var context = new ChangeContextEntry
        {
            Id = $"human-cognitive-seed:{Guid.NewGuid():N}",
            NodeId = BaselineMethod.Id,
            Statement = "Keep the graph boundary explicit.",
            ContextKind = "constraint",
            Provenance = "user-stated",
            UserConfirmed = false,
            ProjectContext = BaselineProjectContext,
            ContentHash = "content-hash",
            TargetSourceHashAtWrite = BaselineMethod.SourceHash,
            TargetUpdatedAtAtWrite = BaselineMethod.UpdatedAt,
            CreatedAt = DateTimeOffset.Parse("2026-08-10T12:00:00Z")
        };

        try
        {
            await contextRepository.UpsertAsync(context);
            await contextRepository.UpsertAsync(context with { TargetSourceHashAtWrite = "retry-must-not-overwrite" });

            (await contextRepository.ListForNodeAsync(BaselineMethod.Id, 10))
                .Should().ContainSingle().Which.Should().BeEquivalentTo(context);
            (await CountMentionsAsync(driver, context.Id, BaselineMethod.Id)).Should().Be(1);
            (await vectorRepository.ListAsync(BaselineProjectContext))
                .Should().NotContain(document => document.Id == context.Id);
            (await vectorRepository.CountAsync(BaselineProjectContext)).Should().Be(0);

            await _repository!.DeleteFileAsync(BaselineProjectContext, BaselineMethod.FilePath!);

            (await contextRepository.ListForNodeAsync(BaselineMethod.Id, 10))
                .Should().ContainSingle("orphaned context remains retrievable by exact target ID");
            (await CountMentionsAsync(driver, context.Id, BaselineMethod.Id)).Should().Be(0);

            await _repository.UpsertNodeAsync(BaselineMethod);

            (await CountMentionsAsync(driver, context.Id, BaselineMethod.Id)).Should().Be(1);
        }
        finally
        {
            await vectorRepository.DeleteProjectAsync(BaselineProjectContext);
        }
    }

    private static async Task<long> CountMentionsAsync(IDriver driver, string contextId, string nodeId)
    {
        await using var session = driver.AsyncSession();
        var cursor = await session.RunAsync(
            "MATCH (:HumanCognitiveSeedContext {id: $contextId})-[r:Mentions]->(:CodeNode {id: $nodeId}) RETURN count(r) AS count",
            new { contextId, nodeId });
        var record = await cursor.SingleAsync();
        return Convert.ToInt64(record["count"], System.Globalization.CultureInfo.InvariantCulture);
    }
}
