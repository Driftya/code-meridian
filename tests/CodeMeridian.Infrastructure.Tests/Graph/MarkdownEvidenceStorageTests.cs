using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CodeMeridian.Core.Projects;
using CodeMeridian.Core.RepositoryConstraints;
using CodeMeridian.Infrastructure.Graph;
using FluentAssertions;
using Neo4j.Driver;
using NSubstitute;

namespace CodeMeridian.Infrastructure.Tests.Graph;

public sealed class MarkdownEvidenceStorageTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task AtomicPublicationIncludesOwnedFactsAndLegacyReplacementRemovesThem()
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
        var writes = new List<(string Query, JsonElement Parameters)>();
        transaction.RunAsync(Arg.Any<string>(), Arg.Any<object>()).Returns(call =>
        {
            writes.Add((call.Arg<string>(), JsonSerializer.SerializeToElement(call.Arg<object>())));
            return harness.Cursor;
        });
        var snapshot = Snapshot();
        var repository = new Neo4jProjectProfileRepository(harness.Driver);
        await repository.PublishAsync(snapshot);
        await repository.PublishAsync(snapshot with { MarkdownEvidence = null });
        var publications = writes.Where(write => write.Query.Contains("s.markdownJson = $markdownJson", StringComparison.Ordinal)).ToArray();
        publications.Should().HaveCount(2);
        publications[0].Query.Should().Contain("s.generation = $generation").And.Contain("s.outcomesJson = '[]'");
        JsonSerializer.Deserialize<MarkdownReferenceEvidence>(publications[0].Parameters.GetProperty("markdownJson").GetString()!, Options)
            .Should().BeEquivalentTo(snapshot.MarkdownEvidence);
        publications[1].Parameters.GetProperty("markdownJson").ValueKind.Should().Be(JsonValueKind.Null);
        writes.Should().NotContain(write => write.Query.Contains("KnowledgeDocument", StringComparison.Ordinal)
            || write.Query.Contains("CodeNode", StringComparison.Ordinal), "profile publication owns neither manual documents nor code facts");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExactRetryIsIdempotentAndChangedFactsConflictBeforeAnyReplacement(bool changed)
    {
        var snapshot = Snapshot();
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(snapshot, Options))));
        var harness = new Neo4jRepositoryTestHarness();
        harness.Driver.AsyncSession().Returns(harness.Session);
        var transaction = Substitute.For<IAsyncQueryRunner>();
        harness.Session.ExecuteWriteAsync(Arg.Any<Func<IAsyncQueryRunner, Task>>(), Arg.Any<Action<TransactionConfigBuilder>>())
            .Returns(call => call.Arg<Func<IAsyncQueryRunner, Task>>()(transaction));
        var record = Substitute.For<IRecord>();
        record["latest"].Returns(7L);
        record["published"].Returns(7L);
        record["fingerprint"].Returns(fingerprint);
        harness.Cursor.FetchAsync().Returns(true);
        harness.Cursor.Current.Returns(record);
        transaction.RunAsync(Arg.Any<string>(), Arg.Any<object>()).Returns(harness.Cursor);
        if (changed) snapshot = snapshot with { MarkdownEvidence = snapshot.MarkdownEvidence! with { Status = "incomplete" } };
        var publish = () => new Neo4jProjectProfileRepository(harness.Driver).PublishAsync(snapshot);
        if (changed) await publish.Should().ThrowAsync<ProjectProfileConflictException>();
        else await publish.Should().NotThrowAsync();
        await transaction.Received(1).RunAsync(Arg.Any<string>(), Arg.Any<object>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadsPreserveFileFactsAndRejectCorruptedBindings(bool corrupt)
    {
        var snapshot = Snapshot();
        var harness = new Neo4jRepositoryTestHarness();
        harness.Driver.AsyncSession().Returns(harness.Session);
        var transaction = Substitute.For<IAsyncQueryRunner>();
        harness.Session.ExecuteReadAsync(Arg.Any<Func<IAsyncQueryRunner, Task<StoredRepositoryConstraintSnapshot?>>>(), Arg.Any<Action<TransactionConfigBuilder>>())
            .Returns(call => call.Arg<Func<IAsyncQueryRunner, Task<StoredRepositoryConstraintSnapshot?>>>()(transaction));
        var record = Substitute.For<IRecord>();
        record["generation"].Returns(7L);
        record["latest"].Returns(7L);
        record["profile"].Returns(JsonSerializer.Serialize(snapshot.Profile, Options));
        record["constraints"].Returns((string)null!);
        record["markdown"].Returns(JsonSerializer.Serialize(corrupt ? snapshot.MarkdownEvidence! with { SourceFingerprint = "bad" } : snapshot.MarkdownEvidence, Options));
        record["files"].Returns(new List<IDictionary<string, object>> { new Dictionary<string, object> { ["path"] = "a.md", ["kind"] = "markdown" } });
        harness.Cursor.FetchAsync().Returns(true);
        harness.Cursor.Current.Returns(record);
        transaction.RunAsync(Arg.Any<string>(), Arg.Any<object>()).Returns(harness.Cursor);
        var read = () => new Neo4jProjectProfileRepository(harness.Driver).GetRepositoryConstraintsAsync("Docs");
        if (corrupt) await read.Should().ThrowAsync<InvalidOperationException>();
        else (await read())!.MarkdownEvidence.Should().BeEquivalentTo(snapshot.MarkdownEvidence);
    }

    private static ProjectProfileSnapshot Snapshot()
    {
        var hash = "sha256:" + new string('a', 64);
        return new(7, new("Docs", DateTimeOffset.UtcNow, "complete", 1, [new("markdown", 1, ["a.md"])], [], 0, [], [], 0)
            { SourceFingerprint = hash }, [new("a.md", "markdown")])
        {
            MarkdownEvidence = new(hash, hash, "complete", [new("Docs", "Docs::doc::a.md", "a.md", 7, hash, hash, "complete", [], [])])
        };
    }
}
