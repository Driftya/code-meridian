using System.Text.Json;
using CodeMeridian.Core.Projects;
using CodeMeridian.Core.RepositoryConstraints;
using CodeMeridian.Infrastructure.Graph;
using FluentAssertions;
using Neo4j.Driver;
using NSubstitute;

namespace CodeMeridian.Infrastructure.Tests.Graph;

public sealed class RepositoryConstraintStorageTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadIsBoundedAndProjectGenerationScopedAndPreservesLegacyMissingEvidence(bool legacy)
    {
        var harness = new Neo4jRepositoryTestHarness();
        harness.Driver.AsyncSession().Returns(harness.Session);
        var transaction = Substitute.For<IAsyncQueryRunner>();
        harness.Session.ExecuteReadAsync(Arg.Any<Func<IAsyncQueryRunner, Task<StoredRepositoryConstraintSnapshot?>>>(),
                Arg.Any<Action<TransactionConfigBuilder>>())
            .Returns(call => call.Arg<Func<IAsyncQueryRunner, Task<StoredRepositoryConstraintSnapshot?>>>()(transaction));
        var snapshot = Snapshot();
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var record = Substitute.For<IRecord>();
        record["generation"].Returns(7L);
        record["latest"].Returns(8L);
        record["profile"].Returns(JsonSerializer.Serialize(snapshot.Profile, options));
        record["constraints"].Returns(legacy ? null! : JsonSerializer.Serialize(snapshot.ConstraintEvidence, options));
        record["files"].Returns(new List<IDictionary<string, object>> { new Dictionary<string, object> { ["path"] = "policy.json", ["kind"] = "configuration" } });
        harness.Cursor.FetchAsync().Returns(true);
        harness.Cursor.Current.Returns(record);
        string? query = null;
        object? parameters = null;
        transaction.RunAsync(Arg.Do<string>(value => query = value), Arg.Do<object>(value => parameters = value)).Returns(harness.Cursor);

        var stored = await new Neo4jProjectProfileRepository(harness.Driver).GetRepositoryConstraintsAsync("Docs");

        stored!.Generation.Should().Be(7);
        stored.LatestGeneration.Should().Be(8);
        stored.Files.Should().Equal(snapshot.Files);
        if (legacy) stored.Evidence.Should().BeNull();
        else stored.Evidence.Should().BeEquivalentTo(snapshot.ConstraintEvidence);
        JsonSerializer.SerializeToElement(parameters).GetProperty("project").GetString().Should().Be("Docs");
        query.Should().Contain("generation: s.generation").And.Contain("project: $project").And.Contain("LIMIT 100001");
        query!.IndexOf("LIMIT 100001", StringComparison.Ordinal).Should().BeLessThan(query.IndexOf("RETURN collect", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PublicationStoresLosslessEvidenceInTheSameGenerationWriteAndClearsItForLegacyReplacement()
    {
        var harness = new Neo4jRepositoryTestHarness();
        harness.Driver.AsyncSession().Returns(harness.Session);
        var transaction = Substitute.For<IAsyncQueryRunner>();
        harness.Session.ExecuteWriteAsync(Arg.Any<Func<IAsyncQueryRunner, Task>>(), Arg.Any<Action<TransactionConfigBuilder>>())
            .Returns(call => call.Arg<Func<IAsyncQueryRunner, Task>>()(transaction));
        var record = Substitute.For<IRecord>();
        record["latest"].Returns(7L);
        record["published"].Returns(0L);
        record["fingerprint"].Returns((string)null!);
        harness.Cursor.FetchAsync().Returns(true);
        harness.Cursor.Current.Returns(record);
        var writes = new List<JsonElement>();
        transaction.RunAsync(Arg.Any<string>(), Arg.Any<object>()).Returns(call =>
        {
            if (call.Arg<string>().Contains("s.constraintJson = $constraintJson", StringComparison.Ordinal))
                writes.Add(JsonSerializer.SerializeToElement(call.Arg<object>()));
            return harness.Cursor;
        });
        var repository = new Neo4jProjectProfileRepository(harness.Driver);
        var snapshot = Snapshot();
        await repository.PublishAsync(snapshot);
        await repository.PublishAsync(snapshot with { ConstraintEvidence = null });

        writes.Should().HaveCount(2);
        var json = writes[0].GetProperty("constraintJson").GetString()!;
        JsonSerializer.Deserialize<RepositoryConstraintEvidence>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            .Should().BeEquivalentTo(snapshot.ConstraintEvidence);
        writes[0].GetProperty("generation").GetInt64().Should().Be(7);
        writes[1].GetProperty("constraintJson").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task CancelledReadsDoNotOpenASession()
    {
        var harness = new Neo4jRepositoryTestHarness();
        await FluentActions.Awaiting(() => new Neo4jProjectProfileRepository(harness.Driver)
            .GetRepositoryConstraintsAsync("Docs", new(true))).Should().ThrowAsync<OperationCanceledException>();
        harness.Driver.DidNotReceive().AsyncSession();
    }

    [Fact]
    public async Task OverflowSentinelFailsInsteadOfReturningAnApparentlyCompleteTruncatedInventory()
    {
        var harness = new Neo4jRepositoryTestHarness();
        harness.Driver.AsyncSession().Returns(harness.Session);
        var transaction = Substitute.For<IAsyncQueryRunner>();
        harness.Session.ExecuteReadAsync(Arg.Any<Func<IAsyncQueryRunner, Task<StoredRepositoryConstraintSnapshot?>>>(),
                Arg.Any<Action<TransactionConfigBuilder>>())
            .Returns(call => call.Arg<Func<IAsyncQueryRunner, Task<StoredRepositoryConstraintSnapshot?>>>()(transaction));
        var record = Substitute.For<IRecord>();
        var file = new Dictionary<string, object> { ["path"] = "a.md", ["kind"] = "markdown" };
        record["files"].Returns(Enumerable.Repeat<IDictionary<string, object>>(file, 100001).ToList());
        harness.Cursor.FetchAsync().Returns(true);
        harness.Cursor.Current.Returns(record);
        transaction.RunAsync(Arg.Any<string>(), Arg.Any<object>()).Returns(harness.Cursor);

        await FluentActions.Awaiting(() => new Neo4jProjectProfileRepository(harness.Driver).GetRepositoryConstraintsAsync("Docs"))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage("*limit*");
    }

    private static ProjectProfileSnapshot Snapshot()
    {
        var hash = "sha256:" + new string('a', 64);
        return new(7, new("Docs", DateTimeOffset.UtcNow, "complete", 1,
            [new("configuration", 1, ["policy.json"])], [], 0, [], [], 0)
            { SourceFingerprint = hash, ConfigurationFingerprint = hash }, [new("policy.json", "configuration")])
        {
            ConstraintEvidence = new("policy.json", hash, true, new("1.0", [new("docs", ["**/*.md"])],
                [new("location", "docs", ["docs/**"], [], new string('a', 200), "error", [])], []), [])
        };
    }
}
