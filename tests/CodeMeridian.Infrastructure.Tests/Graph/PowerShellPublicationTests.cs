using System.Security.Cryptography;
using System.Text.Json;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Projects;
using CodeMeridian.Infrastructure.Graph;
using FluentAssertions;
using Neo4j.Driver;
using NSubstitute;

namespace CodeMeridian.Infrastructure.Tests.Graph;

public sealed class PowerShellPublicationTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    private const string Hash = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Theory]
    [InlineData("new")]
    [InlineData("retry")]
    [InlineData("conflict")]
    [InlineData("stale")]
    [InlineData("source")]
    [InlineData("inventory")]
    [InlineData("collision")]
    public async Task PublicationValidatesBindingsBeforeReplacingOnlyOwnedFacts(string mode)
    {
        var snapshot = Snapshot();
        var harness = new Neo4jRepositoryTestHarness();
        harness.Driver.AsyncSession().Returns(harness.Session);
        var tx = Substitute.For<IAsyncQueryRunner>();
        harness.Session.ExecuteWriteAsync(Arg.Any<Func<IAsyncQueryRunner, Task>>(), Arg.Any<Action<TransactionConfigBuilder>>())
            .Returns(call => call.Arg<Func<IAsyncQueryRunner, Task>>()(tx));
        var record = Substitute.For<IRecord>();
        record["generation"].Returns(3L);
        record["latest"].Returns(mode == "stale" ? 4L : 3L);
        record["powershellGeneration"].Returns(mode is "retry" or "conflict" ? 3L : null!);
        record["powershellFingerprint"].Returns(mode == "retry"
            ? Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(snapshot, Options))) : "different");
        record["profile"].Returns(JsonSerializer.Serialize(new ProjectProfile("Test", DateTimeOffset.UtcNow, "complete", 1, [], [], 0,
            [new("powershell", true, "enabled", "unknown", [], []) { Fingerprint = snapshot.Fingerprint }], [], 0)
            { SourceFingerprint = mode == "source" ? "other" : Hash }, Options));
        var writes = new List<(string Query, JsonElement Parameters)>();
        tx.RunAsync(Arg.Any<string>(), Arg.Any<object>()).Returns(call =>
        {
            var query = call.Arg<string>();
            writes.Add((query, JsonSerializer.SerializeToElement(call.Arg<object>())));
            var cursor = Substitute.For<IResultCursor>();
            var row = record;
            if (query.Contains("collect(f.path)"))
            {
                row = Substitute.For<IRecord>();
                row["paths"].Returns(mode == "inventory" ? new List<string> { "other.ps1" } : new List<string> { "a.ps1" });
            }
            if (query.Contains("AS collisions"))
            {
                row = Substitute.For<IRecord>();
                row["collisions"].Returns(mode == "collision" ? 1L : 0L);
            }
            cursor.FetchAsync().Returns(true, false);
            cursor.Current.Returns(row);
            cursor.GetAsyncEnumerator(Arg.Any<CancellationToken>()).Returns(_ => Enumerate(row).GetAsyncEnumerator());
            return cursor;
        });
        var publish = () => new Neo4jProjectProfileRepository(harness.Driver).PublishPowerShellGraphAsync(snapshot);
        if (mode is "new" or "retry") await publish.Should().NotThrowAsync();
        else await publish.Should().ThrowAsync<ProjectProfileConflictException>();
        var deletes = writes.Where(write => write.Query.Contains("DELETE ")).ToArray();
        if (mode != "new") deletes.Should().BeEmpty();
        else
        {
            deletes.Should().HaveCount(2);
            deletes.Should().OnlyContain(write => write.Query.Contains("powershellProject"));
            deletes.Single(write => write.Query.Contains("DELETE n")).Query.Should().Contain("NOT (n)--()");
            writes.Should().Contain(write => write.Query.Contains("n.sourceSnippet = $snippet"));
            writes.Should().Contain(write => write.Query.Contains("s.powershellFingerprint = $fingerprint"));
        }
    }

    [Fact]
    public async Task CancelledPublicationDoesNotReadOrModifyState()
    {
        var harness = new Neo4jRepositoryTestHarness();
        harness.Driver.AsyncSession().Returns(harness.Session);
        var tx = Substitute.For<IAsyncQueryRunner>();
        harness.Session.ExecuteWriteAsync(Arg.Any<Func<IAsyncQueryRunner, Task>>(), Arg.Any<Action<TransactionConfigBuilder>>())
            .Returns(call => call.Arg<Func<IAsyncQueryRunner, Task>>()(tx));
        var publish = () => new Neo4jProjectProfileRepository(harness.Driver).PublishPowerShellGraphAsync(Snapshot(), new(true));
        await publish.Should().ThrowAsync<OperationCanceledException>();
        await tx.DidNotReceiveWithAnyArgs().RunAsync(default!, default(object)!);
    }

    private static PowerShellGraphSnapshot Snapshot() => new("Test", 3, Hash, new(Hash, Hash),
        [new("a.ps1", "complete", Hash, [new() { Id = "Test::PowerShellFile::a", Name = "a.ps1", Type = CodeNodeType.File,
            ProjectContext = "Test", FilePath = "a.ps1", SourceHash = Hash }], [])]);

    private static async IAsyncEnumerable<IRecord> Enumerate(IRecord record)
    {
        await Task.CompletedTask;
        yield return record;
    }
}
