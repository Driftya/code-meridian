using CodeMeridian.Application.Projects;
using CodeMeridian.Application.Services;
using CodeMeridian.Core.KeywordGraph;
using CodeMeridian.Core.Projects;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace CodeMeridian.Application.Tests.Services;

public sealed class KeywordCapabilityGuardTests
{
    [Fact]
    public async Task LexicalReadsRequirePublishedKeywordEvidenceForTheExactSourceProject()
    {
        var keywords = Substitute.For<IKeywordGraphRepository>();
        keywords.GetKeywordSourceNodesByIdAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns([new KeywordSourceNode { Id = "source", Kind = "Class", ProjectContext = "Fixture" }]);
        var profiles = Substitute.For<IProjectProfileRepository>();
        var profile = ToolCapabilityPolicyTests.Profile();
        profiles.GetAsync("Fixture", null, Arg.Any<CancellationToken>()).Returns(new StoredProjectProfile(1, 1, DateTimeOffset.UtcNow, profile.Profile!, null)
            { AnalyzerOutcomes = profile.AnalyzerOutcomes });
        var service = Service(keywords, profiles);

        var result = await service.FindRelatedKnowledgeAsync("source");

        result.Should().Contain("status: unknown").And.Contain("keywords");
        await keywords.DidNotReceiveWithAnyArgs().FindRelatedByKeywordsAsync(default!, default);
    }

    [Fact]
    public async Task UnscopedSourcesAndCancellationDoNotInvokeLexicalTraversal()
    {
        var keywords = Substitute.For<IKeywordGraphRepository>();
        var profiles = Substitute.For<IProjectProfileRepository>();
        var service = Service(keywords, profiles);

        (await service.FindRelatedKnowledgeAsync("unresolved")).Should().Contain("status: unknown");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var action = () => service.FindRelatedKnowledgeAsync("source", cancellationToken: cancellation.Token);
        await action.Should().ThrowAsync<OperationCanceledException>();
        await keywords.DidNotReceiveWithAnyArgs().FindRelatedByKeywordsAsync(default!, default);
        await profiles.DidNotReceiveWithAnyArgs().GetAsync(default!, default, default);
    }

    private static KeywordGraphService Service(IKeywordGraphRepository keywords, IProjectProfileRepository profiles) => new(
        keywords, Substitute.For<IKeywordExtractionService>(), Options.Create(new KeywordEnrichmentOptions()),
        Options.Create(new KeywordClassificationOptions()), NullLogger<KeywordGraphService>.Instance, new ProjectProfileService(profiles));
}
