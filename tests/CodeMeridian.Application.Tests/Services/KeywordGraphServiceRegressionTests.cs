using CodeMeridian.Application.Services;
using CodeMeridian.Core.KeywordGraph;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace CodeMeridian.Application.Tests.Services;

public sealed class KeywordGraphServiceRegressionTests
{
    [Fact]
    public async Task RebuildKeywordGraphAsync_WhenChangedNodeExtractsNoKeywords_StillUpdatesChecksumAndCompletes()
    {
        var repository = Substitute.For<IKeywordGraphRepository>();
        var extraction = Substitute.For<IKeywordExtractionService>();
        var source = new KeywordSourceNode
        {
            Id = "node-empty",
            Kind = "KnowledgeDocument",
            ExistingChecksum = "old",
            TextBySource = new Dictionary<string, string?> { ["content"] = "the and for with" }
        };

        repository.GetKeywordSourceNodesAsync(Arg.Any<KeywordSourceNodeQuery>(), Arg.Any<CancellationToken>())
            .Returns([source]);
        extraction.Extract(source).Returns(new KeywordExtractionResult("new", []));

        var sut = new KeywordGraphService(
            repository,
            extraction,
            Options.Create(new KeywordEnrichmentOptions()),
            Options.Create(new KeywordClassificationOptions()),
            NullLogger<KeywordGraphService>.Instance);

        var result = await sut.RebuildKeywordGraphAsync("CodeMeridian");

        await repository.Received(1).ReplaceKeywordsAsync(
            Arg.Is<ReplaceKeywordRelationshipsCommand>(command =>
                command.SourceNodeId == "node-empty"
                && command.KeywordTextChecksum == "new"
                && command.Keywords.Count == 0),
            Arg.Any<CancellationToken>());
        result.Should().Contain("Updated **1** nodes");
    }
}
