using CodeMeridian.Application.Projects;
using CodeMeridian.Application.Services;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;
using CodeMeridian.Core.Projects;
using FluentAssertions;
using NSubstitute;

namespace CodeMeridian.Application.Tests.Services;

public sealed class PrContextReportCapabilityTests
{
    private readonly IProjectProfileRepository profiles = Substitute.For<IProjectProfileRepository>();
    private readonly ICodeGraphRepository graph = Substitute.For<ICodeGraphRepository>();
    private readonly IVectorRepository vector = Substitute.For<IVectorRepository>();

    [Fact]
    public async Task DisabledDocumentsDoNotRequireOrReadDocumentEvidence()
    {
        Publish();

        var report = await Service().BuildAsync(new("Fixture", ["src/A.cs"], IncludeDocs: false));

        report.Applicability!.Status.Should().Be("ok");
        report.Applicability.Coverage.Should().Be("complete");
        await graph.Received(1).QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>());
        await vector.DidNotReceiveWithAnyArgs().ListAsync(default, default, default);
    }

    [Fact]
    public async Task RequestedDocumentsCannotRunWithoutDocumentEvidence()
    {
        Publish();

        var report = await Service().BuildAsync(new("Fixture", ["src/A.cs"]));

        report.Applicability!.Status.Should().Be("unknown");
        report.ReviewFocus.Should().ContainSingle().Which.Should().Contain("documents");
        report.ChangedNodes.Should().BeEmpty();
        await graph.DidNotReceiveWithAnyArgs().QueryNodesAsync(default!, default);
    }

    [Fact]
    public async Task RequestedAuditedDocumentsAreReadAndTheirCoverageIsRevalidated()
    {
        var stored = Publish();
        stored = stored with
        {
            Profile = stored.Profile with { FileKinds = [.. stored.Profile.FileKinds, new("markdown", 1, ["docs/feature.md"])],
                Analyzers = [.. stored.Profile.Analyzers, new("documents", true, "enabled", "unknown", ["document_search"], [])] },
            AnalyzerOutcomes = [.. stored.AnalyzerOutcomes, ToolCapabilityPolicyTests.Outcome() with { Analyzer = "documents" }]
        };
        profiles.GetAsync("Fixture", Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(stored);
        vector.ListAsync("Fixture", 250, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            profiles.GetAsync("Fixture", Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(stored with
            { AnalyzerOutcomes = [stored.AnalyzerOutcomes[0], stored.AnalyzerOutcomes[1] with { State = "failed", Coverage = "unknown" }] });
            return Task.FromResult<IReadOnlyList<KnowledgeDocument>>([]);
        });

        var report = await Service().BuildAsync(new("Fixture", ["src/Feature.cs"]));

        await vector.Received(1).ListAsync("Fixture", 250, Arg.Any<CancellationToken>());
        report.Applicability!.Status.Should().Be("stale");
        report.RelatedDocuments.Should().BeEmpty();
    }

    [Fact]
    public async Task MissingRequiredHistoryBlocksEvenWhenDocumentsAreDisabled()
    {
        Publish(["callable_definitions", "callable_dependencies", "test_to_code"]);

        var report = await Service().BuildAsync(new("Fixture", ["src/A.cs"], IncludeDocs: false));

        report.Applicability!.Status.Should().Be("unknown");
        report.ReviewFocus.Should().ContainSingle().Which.Should().Contain("change_history");
        await graph.DidNotReceiveWithAnyArgs().QueryNodesAsync(default!, default);
    }

    [Fact]
    public async Task NewGenerationDiscardsCompletedReport()
    {
        var stored = Publish();
        graph.QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            profiles.GetAsync("Fixture", Arg.Any<string?>(), Arg.Any<CancellationToken>())
                .Returns(stored with { LatestGeneration = 2 });
            return Task.FromResult<IReadOnlyList<CodeNode>>([]);
        });

        var report = await Service().BuildAsync(new("Fixture", ["src/A.cs"], IncludeDocs: false));

        report.Applicability!.Status.Should().Be("stale");
        report.ReviewFocus.Should().ContainSingle().Which.Should().Contain("discarded");
        report.ChangedNodes.Should().BeEmpty();
    }

    [Fact]
    public async Task QueryFailureIsExplicitAndDoesNotPublishAnEmptySuccess()
    {
        Publish();
        graph.QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<CodeNode>>(new InvalidOperationException("private backend details")));

        var report = await Service().BuildAsync(new("Fixture", ["src/A.cs"], IncludeDocs: false));

        report.Applicability!.Status.Should().Be("failed");
        report.ReviewFocus.Should().ContainSingle().Which.Should().NotContain("private backend details");
    }

    [Fact]
    public async Task CancellationPropagates()
    {
        Publish();
        graph.QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<CodeNode>>(new OperationCanceledException()));

        var action = () => Service().BuildAsync(new("Fixture", ["src/A.cs"], IncludeDocs: false));

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task MissingProjectDoesNotChooseAnotherProjectsEvidence()
    {
        var report = await Service().BuildAsync(new(null, ["src/A.cs"], IncludeDocs: false));

        report.Applicability!.Status.Should().Be("unknown");
        await profiles.DidNotReceiveWithAnyArgs().GetAsync(default!, default, default);
        await graph.DidNotReceiveWithAnyArgs().QueryNodesAsync(default!, default);
    }

    private StoredProjectProfile Publish(string[]? capabilities = null)
    {
        var profile = ToolCapabilityPolicyTests.Profile(capabilities: capabilities ??
            ["callable_definitions", "callable_dependencies", "test_to_code", "change_history"]);
        var stored = new StoredProjectProfile(1, 1, DateTimeOffset.UtcNow, profile.Profile!, null)
        { AnalyzerOutcomes = profile.AnalyzerOutcomes };
        profiles.GetAsync("Fixture", Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(stored);
        return stored;
    }

    private PrContextReportService Service() => new(graph, vector, Substitute.For<IKeywordExtractionService>(), new ProjectProfileService(profiles));
}
