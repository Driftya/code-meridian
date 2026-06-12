using CodeMeridian.Application.Services;
using CodeMeridian.Core.KeywordGraph;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace CodeMeridian.Application.Tests.Services;

public sealed class KeywordGraphServiceTests
{
    [Fact]
    public async Task RebuildKeywordGraphAsync_SkipsUnchangedNodes_AndUpdatesChangedNodes()
    {
        var repository = Substitute.For<IKeywordGraphRepository>();
        var extraction = Substitute.For<IKeywordExtractionService>();
        var options = Options.Create(new KeywordEnrichmentOptions
        {
            BatchSize = 10
        });
        var unchanged = new KeywordSourceNode
        {
            Id = "node-1",
            Kind = "CodeNode",
            ExistingChecksum = "same",
            TextBySource = new Dictionary<string, string?> { ["name"] = "OrderService" }
        };
        var changed = new KeywordSourceNode
        {
            Id = "node-2",
            Kind = "KnowledgeDocument",
            ExistingChecksum = "old",
            TextBySource = new Dictionary<string, string?> { ["content"] = "Authentication design" }
        };

        repository.GetKeywordSourceNodesAsync(Arg.Any<KeywordSourceNodeQuery>(), Arg.Any<CancellationToken>())
            .Returns([unchanged, changed]);
        extraction.Extract(unchanged).Returns(new KeywordExtractionResult("same", []));
        extraction.Extract(changed).Returns(new KeywordExtractionResult(
            "new",
            [
                new ExtractedKeyword
                {
                    Value = "authentication",
                    NormalizedValue = "authentication",
                    Count = 2,
                    Weight = 1.2d,
                    Sources = ["content"]
                }
            ]));

        var sut = new KeywordGraphService(repository, extraction, options, NullLogger<KeywordGraphService>.Instance);

        var result = await sut.RebuildKeywordGraphAsync("CodeMeridian");

        await repository.Received(1).ReplaceKeywordsAsync(
            Arg.Is<ReplaceKeywordRelationshipsCommand>(command =>
                command.SourceNodeId == "node-2"
                && command.KeywordTextChecksum == "new"
                && command.Keywords.Count == 1),
            Arg.Any<CancellationToken>());
        await repository.Received(1).RecalculateKeywordStatisticsAsync("CodeMeridian", Arg.Any<CancellationToken>());
        result.Should().Contain("Processed **2** source nodes");
        result.Should().Contain("Skipped **1** unchanged nodes");
        result.Should().Contain("Updated **1** nodes");
    }

    [Fact]
    public async Task RebuildKeywordGraphAsync_WhenDisabled_ReturnsGuidance()
    {
        var repository = Substitute.For<IKeywordGraphRepository>();
        var extraction = Substitute.For<IKeywordExtractionService>();
        var sut = new KeywordGraphService(
            repository,
            extraction,
            Options.Create(new KeywordEnrichmentOptions { Enabled = false }),
            NullLogger<KeywordGraphService>.Instance);

        var result = await sut.RebuildKeywordGraphAsync();

        result.Should().Contain("Keyword enrichment is disabled");
        await repository.DidNotReceiveWithAnyArgs().GetKeywordSourceNodesAsync(default!, default);
    }

    [Fact]
    public async Task FindRelatedKnowledgeAsync_WithMatches_ReturnsRankedMarkdownAndKeywords()
    {
        var repository = Substitute.For<IKeywordGraphRepository>();
        repository.FindRelatedByKeywordsAsync(Arg.Any<KeywordRelatedNodeQuery>(), Arg.Any<CancellationToken>())
            .Returns([
                new KeywordRelatedNode
                {
                    TargetNodeId = "doc-1",
                    TargetKind = "KnowledgeDocument",
                    SharedKeywordCount = 4,
                    Score = 0.74d,
                    MatchedKeywords = ["stale", "knowledge", "document", "mention"]
                }
            ]);
        var sut = new KeywordGraphService(
            repository,
            Substitute.For<IKeywordExtractionService>(),
            Options.Create(new KeywordEnrichmentOptions()),
            NullLogger<KeywordGraphService>.Instance);

        var result = await sut.FindRelatedKnowledgeAsync("node-1", ["KnowledgeDocument"], limit: 5);

        await repository.Received(1).FindRelatedByKeywordsAsync(
            Arg.Is<KeywordRelatedNodeQuery>(query =>
                query.SourceNodeId == "node-1"
                && query.TargetKinds.SequenceEqual(new[] { "KnowledgeDocument" })
                && query.Limit == 5),
            Arg.Any<CancellationToken>());
        result.Should().Contain("## Related Knowledge");
        result.Should().Contain("Confidence: `lexical`");
        result.Should().Contain("`doc-1`");
        result.Should().Contain("`stale`");
    }
}
