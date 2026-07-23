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

        var classificationOptions = Options.Create(new KeywordClassificationOptions());
        var sut = new KeywordGraphService(repository, extraction, options, classificationOptions, NullLogger<KeywordGraphService>.Instance);

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
            Options.Create(new KeywordClassificationOptions()),
            NullLogger<KeywordGraphService>.Instance);

        var result = await sut.RebuildKeywordGraphAsync();

        result.Should().Contain("Keyword enrichment is disabled");
        await repository.DidNotReceiveWithAnyArgs().GetKeywordSourceNodesAsync(default!, default);
    }

    [Fact]
    public async Task RefreshKeywordsAsync_ProcessesOnlyRequestedNodesAndRecalculatesStatistics()
    {
        var repository = Substitute.For<IKeywordGraphRepository>();
        var extraction = Substitute.For<IKeywordExtractionService>();
        var sourceNode = new KeywordSourceNode
        {
            Id = "node-2",
            ProjectContext = "CodeMeridian",
            Kind = "CodeNode",
            ExistingChecksum = "old",
            TextBySource = new Dictionary<string, string?> { ["summary"] = "Queue based keyword refresh" }
        };

        repository
            .GetKeywordSourceNodesByIdAsync(
                Arg.Is<IReadOnlyCollection<string>>(ids => ids.SequenceEqual(new[] { "node-2" })),
                "CodeMeridian",
                Arg.Any<CancellationToken>())
            .Returns([sourceNode]);

        extraction.Extract(sourceNode).Returns(new KeywordExtractionResult(
            "new",
            [
                new ExtractedKeyword
                {
                    Value = "queue",
                    NormalizedValue = "queue",
                    Count = 1,
                    Weight = 1,
                    Sources = ["summary"]
                }
            ]));

        var sut = new KeywordGraphService(
            repository,
            extraction,
            Options.Create(new KeywordEnrichmentOptions()),
            Options.Create(new KeywordClassificationOptions()),
            NullLogger<KeywordGraphService>.Instance);

        var result = await sut.RefreshKeywordsAsync(["node-2", "node-2"], "CodeMeridian");

        await repository.Received(1).ReplaceKeywordsAsync(
            Arg.Is<ReplaceKeywordRelationshipsCommand>(command =>
                command.SourceNodeId == "node-2"
                && command.KeywordTextChecksum == "new"
                && command.Keywords.Count == 1),
            Arg.Any<CancellationToken>());
        await repository.Received(1).RecalculateKeywordStatisticsAsync("CodeMeridian", Arg.Any<CancellationToken>());
        result.Should().Contain("Queued **1** source nodes");
        result.Should().Contain("Updated **1** nodes");
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
            Options.Create(new KeywordClassificationOptions()),
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
        result.Should().Contain("### Primary matches (1)");
        result.Should().Contain("Confidence: `high`");
    }

    [Fact]
    public async Task FindRelatedKnowledgeAsync_DeduplicatesEquivalentTargetsAndPrunesWeakDefaults()
    {
        var repository = Substitute.For<IKeywordGraphRepository>();
        repository.FindRelatedByKeywordsAsync(Arg.Any<KeywordRelatedNodeQuery>(), Arg.Any<CancellationToken>())
            .Returns([
                new KeywordRelatedNode
                {
                    TargetNodeId = "CodeMeridian::File::docs/features/architecture.md",
                    TargetKind = "KnowledgeDocument",
                    SharedKeywordCount = 4,
                    Score = 0.71d,
                    MatchedKeywords = ["architecture", "drift", "timeline"]
                },
                new KeywordRelatedNode
                {
                    TargetNodeId = "CodeMeridian:File:docs/features/architecture.md",
                    TargetKind = "KnowledgeDocument",
                    SharedKeywordCount = 3,
                    Score = 0.68d,
                    MatchedKeywords = ["architecture", "weather"]
                },
                new KeywordRelatedNode
                {
                    TargetNodeId = "doc-weak",
                    TargetKind = "KnowledgeDocument",
                    SharedKeywordCount = 3,
                    Score = 0.26d,
                    MatchedKeywords = ["code", "feature", "docs"]
                }
            ]);
        var sut = new KeywordGraphService(
            repository,
            Substitute.For<IKeywordExtractionService>(),
            Options.Create(new KeywordEnrichmentOptions()),
            Options.Create(new KeywordClassificationOptions()),
            NullLogger<KeywordGraphService>.Instance);

        var result = await sut.FindRelatedKnowledgeAsync("node-1", ["KnowledgeDocument"], limit: 5);

        result.Should().Contain("Found **1** related nodes: 1 primary, 0 awareness-only.");
        result.Should().Contain("docs/features/architecture.md");
        result.Should().Contain("`architecture`");
        result.Should().Contain("`weather`");
        result.Should().Contain("Pruned **2** weak or duplicate lexical match(es).");
        result.Should().NotContain("doc-weak");
    }

    [Fact]
    public async Task FindRelatedKnowledgeAsync_WithExplicitThresholds_KeepsAwarenessMatches()
    {
        var repository = Substitute.For<IKeywordGraphRepository>();
        repository.FindRelatedByKeywordsAsync(Arg.Any<KeywordRelatedNodeQuery>(), Arg.Any<CancellationToken>())
            .Returns([
                new KeywordRelatedNode
                {
                    TargetNodeId = "doc-strong",
                    TargetKind = "KnowledgeDocument",
                    SharedKeywordCount = 4,
                    Score = 0.74d,
                    MatchedKeywords = ["stale", "knowledge", "graph", "timeline"]
                },
                new KeywordRelatedNode
                {
                    TargetNodeId = "doc-awareness",
                    TargetKind = "KnowledgeDocument",
                    SharedKeywordCount = 3,
                    Score = 0.26d,
                    MatchedKeywords = ["code", "feature", "docs"]
                }
            ]);
        var sut = new KeywordGraphService(
            repository,
            Substitute.For<IKeywordExtractionService>(),
            Options.Create(new KeywordEnrichmentOptions()),
            Options.Create(new KeywordClassificationOptions()),
            NullLogger<KeywordGraphService>.Instance);

        var result = await sut.FindRelatedKnowledgeAsync(
            "node-1",
            ["KnowledgeDocument"],
            minimumSharedKeywords: 3,
            minimumScore: 0.25d,
            limit: 5);

        result.Should().Contain("### Primary matches (1)");
        result.Should().Contain("### Awareness-only matches (1)");
        result.Should().Contain("doc-awareness");
    }

    [Fact]
    public async Task FindRelatedKnowledgeAsync_UbiquitousFrameworkTermsRemainAwarenessOnly()
    {
        var repository = Substitute.For<IKeywordGraphRepository>();
        repository.FindRelatedByKeywordsAsync(Arg.Any<KeywordRelatedNodeQuery>(), Arg.Any<CancellationToken>())
            .Returns([
                new KeywordRelatedNode
                {
                    TargetNodeId = "generic-peer",
                    TargetKind = "Method",
                    SharedKeywordCount = 7,
                    Score = 0.91d,
                    MatchedKeywords = ["cancellation", "token", "async", "task", "graph", "infrastructure", "repository"]
                }
            ]);
        var sut = new KeywordGraphService(
            repository,
            Substitute.For<IKeywordExtractionService>(),
            Options.Create(new KeywordEnrichmentOptions()),
            Options.Create(new KeywordClassificationOptions()),
            NullLogger<KeywordGraphService>.Instance);

        var result = await sut.FindRelatedKnowledgeAsync("diagnostics-cleanup", ["Method"], limit: 5);

        result.Should().Contain("### Primary matches (0)");
        result.Should().Contain("### Awareness-only matches (1)");
        result.Should().Contain("generic-peer");
        result.Should().NotContain("Confidence: `high`");
    }

    [Fact]
    public async Task ClassifyKeywordsAsync_WithKeywords_PersistsClassificationSummary()
    {
        var repository = Substitute.For<IKeywordGraphRepository>();
        repository.GetKeywordSourceNodeCountAsync("CodeMeridian", Arg.Any<CancellationToken>())
            .Returns(10);
        repository.GetKeywordsForClassificationAsync("CodeMeridian", 3, Arg.Any<CancellationToken>())
            .Returns([
                new KeywordForClassification
                {
                    Id = "keyword:codemeridian:mcp",
                    NormalizedValue = "mcp",
                    DocumentFrequency = 2,
                    TotalFrequency = 5
                },
                new KeywordForClassification
                {
                    Id = "keyword:codemeridian:only",
                    NormalizedValue = "only",
                    DocumentFrequency = 4,
                    TotalFrequency = 4
                }
            ]);

        var sut = new KeywordGraphService(
            repository,
            Substitute.For<IKeywordExtractionService>(),
            Options.Create(new KeywordEnrichmentOptions()),
            Options.Create(new KeywordClassificationOptions
            {
                ClassificationVersion = 3
            }),
            NullLogger<KeywordGraphService>.Instance);

        var result = await sut.ClassifyKeywordsAsync("CodeMeridian");

        await repository.Received(1).SaveKeywordClassificationsAsync(
            Arg.Is<IReadOnlyCollection<KeywordClassificationResult>>(items =>
                items.Count == 2
                && items.Any(item => item.KeywordId == "keyword:codemeridian:mcp" && item.Classification == KeywordClassification.ToolingConcept && !item.IsNoise)
                && items.Any(item => item.KeywordId == "keyword:codemeridian:only" && item.Classification == KeywordClassification.Noise && item.IsNoise)),
            3,
            Arg.Any<CancellationToken>());
        result.Should().Contain("## Keyword Classification");
        result.Should().Contain("Processed **2** keywords");
        result.Should().Contain("Marked **1** keywords as noise");
    }

    [Fact]
    public async Task ClassifyKeywordsAsync_WhenClassificationDisabled_ReturnsGuidance()
    {
        var sut = new KeywordGraphService(
            Substitute.For<IKeywordGraphRepository>(),
            Substitute.For<IKeywordExtractionService>(),
            Options.Create(new KeywordEnrichmentOptions()),
            Options.Create(new KeywordClassificationOptions { Enabled = false }),
            NullLogger<KeywordGraphService>.Instance);

        var result = await sut.ClassifyKeywordsAsync("CodeMeridian");

        result.Should().Contain("Keyword classification is disabled");
    }
}
