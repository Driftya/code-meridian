using System.Text.Json;
using CodeMeridian.Core.Knowledge;
using CodeMeridian.Infrastructure.Knowledge;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Neo4j.Driver;
using NSubstitute;

namespace CodeMeridian.Infrastructure.Tests.Knowledge;

public sealed class ScopedDocumentStorageTests
{
    [Theory]
    [InlineData("docs/design notes", null, 1000, 100)]
    [InlineData("docs", "docs/A.md", 0, 1)]
    [InlineData(".", "root.md", 8, 8)]
    public async Task ScopeAndProjectPredicatesPrecedeTheResultLimit(string root, string? target, int requestedLimit, int expectedLimit)
    {
        var harness = new Neo4jRepositoryTestHarness();
        harness.Driver.AsyncSession().Returns(harness.Session);
        string? query = null;
        object? parameters = null;
        var cursor = Cursor([]);
        harness.Session.RunAsync(Arg.Do<string>(value => query = value), Arg.Do<object>(value => parameters = value),
                Arg.Any<Action<TransactionConfigBuilder>>())
            .Returns(cursor);
        var repository = new Neo4jVectorRepository(harness.Driver, 1536, NullLogger<Neo4jVectorRepository>.Instance);

        (await repository.SearchByTextInScopeAsync("chapter?", "Docs & Notes", new(root, target), requestedLimit)).Should().BeEmpty();

        var values = JsonSerializer.SerializeToElement(parameters);
        values.GetProperty("projectContextNormalized").GetString().Should().Be("docs & notes");
        values.GetProperty("rootPath").GetString().Should().Be(root);
        values.GetProperty("rootPrefix").GetString().Should().Be(root + "/");
        values.GetProperty("targetPath").GetString().Should().Be(target);
        values.GetProperty("topK").GetInt32().Should().Be(expectedLimit);
        values.GetProperty("query").GetString().Should().Be("chapter\\?");
        query!.IndexOf("source STARTS WITH $rootPrefix", StringComparison.Ordinal).Should()
            .BeLessThan(query.IndexOf("LIMIT $topK", StringComparison.Ordinal));
        query.IndexOf("source = $targetPath", StringComparison.Ordinal).Should()
            .BeLessThan(query.IndexOf("LIMIT $topK", StringComparison.Ordinal));
        query.Should().Contain("node.projectContextNormalized = $projectContextNormalized")
            .And.Contain("excludedMetadataKind").And.Contain("ORDER BY score DESC, node.id");
    }

    [Fact]
    public async Task StorageRejectsMalformedAndOutsideSourceIdentitiesDefensively()
    {
        var harness = new Neo4jRepositoryTestHarness();
        harness.Driver.AsyncSession().Returns(harness.Session);
        var cursor = Cursor([Record("valid", "docs/a.md"), Record("sibling", "docs-old/a.md"), Record("escape", "docs/../private.md")]);
        harness.Session.RunAsync(Arg.Any<string>(), Arg.Any<object>(), Arg.Any<Action<TransactionConfigBuilder>>()).Returns(cursor);
        var repository = new Neo4jVectorRepository(harness.Driver, 1536, NullLogger<Neo4jVectorRepository>.Instance);

        var documents = await repository.SearchByTextInScopeAsync("chapter", "Docs", new("docs"));

        documents.Should().ContainSingle().Which.Id.Should().Be("valid");
    }

    [Fact]
    public async Task CancellationAndMissingProjectFailBeforeOpeningASession()
    {
        var harness = new Neo4jRepositoryTestHarness();
        var repository = new Neo4jVectorRepository(harness.Driver, 1536, NullLogger<Neo4jVectorRepository>.Instance);
        var invalid = () => repository.SearchByTextInScopeAsync("chapter", " ", new("docs"));
        await invalid.Should().ThrowAsync<ArgumentException>();
        var cancelled = () => repository.SearchByTextInScopeAsync("chapter", "Docs", new("docs"), cancellationToken: new(true));
        await cancelled.Should().ThrowAsync<OperationCanceledException>();
        harness.Driver.DidNotReceive().AsyncSession();
    }

    private static IRecord Record(string id, string source)
    {
        var node = Substitute.For<INode>();
        node.Properties.Returns(new Dictionary<string, object?>
            { ["id"] = id, ["source"] = source, ["content"] = "chapter", ["projectContext"] = "Docs" });
        var record = Substitute.For<IRecord>();
        record["node"].Returns(node);
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
