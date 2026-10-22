using CodeMeridian.Infrastructure.Graph;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Neo4j.Driver;
using NSubstitute;

namespace CodeMeridian.Infrastructure.Tests.Graph;

public sealed class ArchitecturePolicyLoadingTests
{
    [Theory]
    [InlineData("missing")]
    [InlineData("empty")]
    [InlineData("invalid")]
    [InlineData("truncated")]
    public async Task ConfiguredPolicyFailuresNeverExecuteAnAllClearDependencyQuery(string state)
    {
        var harness = new Neo4jRepositoryTestHarness();
        Setup(harness, configured: true, state);
        var repository = new Neo4jCodeGraphRepository(harness.Driver, NullLogger<Neo4jCodeGraphRepository>.Instance);

        var action = () => repository.FindArchitectureViolationsAsync("Fixture");

        await action.Should().ThrowAsync<ArgumentException>();
        await harness.Session.DidNotReceive().RunAsync(Arg.Is<string>(sql => sql.Contains("MATCH (source:CodeNode)")), Arg.Any<IDictionary<string, object?>>());
    }

    [Fact]
    public async Task InvalidDefaultFileIsNotTreatedAsAbsentConfiguration()
    {
        var harness = new Neo4jRepositoryTestHarness();
        Setup(harness, configured: false, "empty");
        var repository = new Neo4jCodeGraphRepository(harness.Driver, NullLogger<Neo4jCodeGraphRepository>.Instance);

        await FluentActions.Awaiting(() => repository.FindSmellPathsAsync("Fixture")).Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task TrulyAbsentUnconfiguredPolicyRetainsLegacyDefaults()
    {
        var harness = new Neo4jRepositoryTestHarness();
        Setup(harness, configured: false, "missing");
        var repository = new Neo4jCodeGraphRepository(harness.Driver, NullLogger<Neo4jCodeGraphRepository>.Instance);

        (await repository.FindArchitectureViolationsAsync("Fixture")).Should().BeEmpty();

        await harness.Session.Received(1).RunAsync(Arg.Is<string>(sql => sql.Contains("MATCH (source:CodeNode)")),
            Arg.Is<IDictionary<string, object?>>(value => HasReason(value, "Core must not depend on Application")));
    }

    [Fact]
    public async Task ValidLegacyPolicyStillDrivesDependencyQueries()
    {
        var harness = new Neo4jRepositoryTestHarness();
        Setup(harness, configured: true, "valid");
        var repository = new Neo4jCodeGraphRepository(harness.Driver, NullLogger<Neo4jCodeGraphRepository>.Instance);

        (await repository.FindArchitectureViolationsAsync("Fixture")).Should().BeEmpty();

        await harness.Session.Received(1).RunAsync(Arg.Is<string>(sql => sql.Contains("MATCH (source:CodeNode)")),
            Arg.Is<IDictionary<string, object?>>(value => HasReason(value, "Domain must not depend on Store")));
    }

    private static bool HasReason(object parameters, string reason) =>
        parameters is Dictionary<string, object?> dictionary && dictionary.TryGetValue("ruleReason0", out var value) && Equals(value, reason);

    private static void Setup(Neo4jRepositoryTestHarness harness, bool configured, string state)
    {
        harness.Driver.AsyncSession().Returns(harness.Session);
        harness.Session.RunAsync(Arg.Any<string>(), Arg.Any<IDictionary<string, object?>>()).Returns(harness.Cursor);
        var pathCursor = Cursor(configured ? [Record(("valuePreview", ".meridian/architecture.json"))] : []);
        harness.Session.RunAsync(Arg.Is<string>(sql => sql.Contains("key.nameNormalized = 'architecture:path'")), Arg.Any<object>())
            .Returns(pathCursor);
        var entries = state switch
        {
            "empty" => new[] { Record(("canonicalKey", null), ("valuePreview", null)) },
            "invalid" => [Record(("canonicalKey", "name"), ("valuePreview", "Invalid policy"))],
            "truncated" => [Record(("canonicalKey", "name"), ("valuePreview", new string('a', 80)))],
            "valid" => new Dictionary<string, string>
            {
                ["layers:0:id"] = "Domain", ["layers:0:namespaceContainsAny:0"] = ".Domain",
                ["layers:1:id"] = "Store", ["layers:1:namespaceContainsAny:0"] = ".Store",
                ["forbiddenDependencies:0:from"] = "Domain", ["forbiddenDependencies:0:to"] = "Store"
            }.Select(item => Record(("canonicalKey", item.Key), ("valuePreview", item.Value))).ToArray(),
            _ => []
        };
        var entriesCursor = Cursor(entries);
        harness.Session.RunAsync(Arg.Is<string>(sql => sql.Contains("AS canonicalKey")), Arg.Any<object>()).Returns(entriesCursor);
    }

    private static IRecord Record(params (string Key, object? Value)[] values)
    {
        var record = Substitute.For<IRecord>();
        foreach (var (key, value) in values) record[key].Returns(value!);
        return record;
    }

    private static IResultCursor Cursor(IRecord[] records)
    {
        var cursor = Substitute.For<IResultCursor>();
        cursor.GetAsyncEnumerator(Arg.Any<CancellationToken>()).Returns(_ => Enumerate(records).GetAsyncEnumerator());
        return cursor;
    }

    private static async IAsyncEnumerable<IRecord> Enumerate(IRecord[] records)
    {
        await Task.CompletedTask;
        foreach (var record in records) yield return record;
    }
}
