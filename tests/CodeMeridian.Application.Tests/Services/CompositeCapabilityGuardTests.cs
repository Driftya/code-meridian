using CodeMeridian.Application.Projects;
using CodeMeridian.Application.Services;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;
using CodeMeridian.Core.Projects;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace CodeMeridian.Application.Tests.Services;

public sealed class CompositeCapabilityGuardTests
{
    private readonly IProjectProfileRepository profiles = Substitute.For<IProjectProfileRepository>();
    private readonly ICodeGraphRepository graph = Substitute.For<ICodeGraphRepository>();
    private readonly IVectorRepository vector = Substitute.For<IVectorRepository>();

    [Fact]
    public async Task FeaturePathWithoutOptionalSectionsRunsWithCallableEvidenceOnly()
    {
        Publish(["callable_definitions", "callable_dependencies"]);
        graph.QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>()).Returns([Node("Feature")]);

        var result = await Service().AnalyzeFeatureImplementationPathAsync("Feature", "Fixture",
            includeTests: false, includeDocs: false);

        result.Should().Contain("status: ok").And.Contain("**Risk level:** unknown")
            .And.NotContain("No matching KnowledgeDocument").And.NotContain("No linked tests were found")
            .And.NotContain("No related test nodes").And.NotContain("without_feature_doc");
        await graph.DidNotReceiveWithAnyArgs().FindRelatedTestsAsync(default!, default, default);
        await vector.DidNotReceiveWithAnyArgs().SearchByTextAsync(default!, default, default, default);
    }

    [Theory]
    [InlineData(true, false, "test_to_code")]
    [InlineData(false, true, "documents")]
    public async Task RequestedUnavailableFeatureSectionsBlockBeforeAnyQueries(bool tests, bool docs, string reason)
    {
        Publish(["callable_definitions", "callable_dependencies"]);

        var result = await Service().AnalyzeFeatureImplementationPathAsync("Feature", "Fixture",
            includeTests: tests, includeDocs: docs);

        result.Should().Contain("status: unknown").And.Contain(reason).And.NotContain("Feature Implementation Path -");
        await graph.DidNotReceiveWithAnyArgs().QueryNodesAsync(default!, default);
        await vector.DidNotReceiveWithAnyArgs().SearchByTextAsync(default!, default, default, default);
    }

    [Fact]
    public async Task ChangedSubgraphCanDisableDocumentsButStillRequiresArchitectureAndHistory()
    {
        Publish(["callable_definitions", "callable_dependencies", "test_to_code", "namespace_classification", "change_history"], architecture: true);
        graph.QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>()).Returns([Node("Feature")]);

        var result = await Service().AnalyzeChangedSubgraphAsync(["src/A.cs"], "Fixture", includeDocs: false);

        result.Should().Contain("status: ok").And.NotContain("Docs and feature notes to review");
        await vector.DidNotReceiveWithAnyArgs().SearchByTextAsync(default!, default, default, default);
        await graph.Received(1).FindArchitectureViolationsAsync("Fixture", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WeatherDoesNotReadUnauditedProjectionOrCountItAsZero()
    {
        Publish(["callable_definitions", "callable_dependencies", "test_to_code", "namespace_classification"], architecture: true);
        graph.CountCodeNodesAsync("Fixture", Arg.Any<CancellationToken>()).Returns(1);

        var result = await Service().GetArchitectureWeatherReportAsync("Fixture");

        result.Should().Contain("status: ok").And.Contain("Bridge nodes: unavailable")
            .And.Contain("graph_projection").And.NotContain("| Bridge nodes | 0 |").And.NotContain("/100");
        await graph.DidNotReceiveWithAnyArgs().GetBetweennessAsync(default, default, default);
    }

    [Fact]
    public async Task DisabledSliceTestPlanSkipsTestsAndUnauditedAdvisorySections()
    {
        Publish(["callable_definitions", "callable_dependencies"]);
        SetUpSlices();

        var result = await Service().SuggestResponsibilitySlicesAsync("Feature", "Fixture",
            includeNamespacePlan: false, includeTestPlan: false);

        result.Should().Contain("status: ok").And.Contain("Responsibility Slice Suggestions")
            .And.Contain("Test evidence was not requested").And.NotContain("test shield missing")
            .And.Contain("Document evidence: unavailable").And.Contain("Community evidence: unavailable");
        await graph.DidNotReceiveWithAnyArgs().FindRelatedTestsAsync(default!, default, default);
        await graph.DidNotReceiveWithAnyArgs().FindNaturalModuleAssignmentsAsync(default!, default, default);
        await vector.DidNotReceiveWithAnyArgs().SearchByTextAsync(default!, default, default, default);
    }

    [Fact]
    public async Task SliceAdvisoryCancellationPropagates()
    {
        Publish(["callable_definitions", "callable_dependencies", "graph_projection"]);
        SetUpSlices();
        graph.FindNaturalModuleAssignmentsAsync(Arg.Any<IReadOnlyCollection<string>>(), "Fixture", Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<(CodeNode, long)>>(new OperationCanceledException()));

        var action = () => Service().SuggestResponsibilitySlicesAsync("Feature", "Fixture",
            includeNamespacePlan: false, includeTestPlan: false);

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task EnabledFeatureRequirementsAreRevalidatedAfterExecution()
    {
        var stored = Publish(["callable_definitions", "callable_dependencies", "test_to_code"]);
        graph.QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            profiles.GetAsync("Fixture", Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(stored with
            {
                Profile = stored.Profile with { Analyzers = [stored.Profile.Analyzers[0] with
                { SupportedCapabilities = ["callable_definitions", "callable_dependencies"] }] }
            });
            return Task.FromResult<IReadOnlyList<CodeNode>>([Node("Feature")]);
        });

        var result = await Service().AnalyzeFeatureImplementationPathAsync("Feature", "Fixture", includeDocs: false);

        result.Should().Contain("status: stale").And.NotContain("Feature Implementation Path -");
    }

    [Fact]
    public async Task ChangedSubgraphStillBlocksMissingHistoryWithDocumentsDisabled()
    {
        Publish(["callable_definitions", "callable_dependencies", "test_to_code", "namespace_classification"], architecture: true);

        var result = await Service().AnalyzeChangedSubgraphAsync(["src/A.cs"], "Fixture", includeDocs: false);

        result.Should().Contain("status: unknown").And.Contain("change_history");
        await graph.DidNotReceiveWithAnyArgs().QueryNodesAsync(default!, default);
    }

    [Fact]
    public async Task AuditedEmptyBridgeAnalysisRetainsAValidZeroAndScore()
    {
        Publish(["callable_definitions", "callable_dependencies", "test_to_code", "namespace_classification", "graph_projection"], architecture: true);
        graph.CountCodeNodesAsync("Fixture", Arg.Any<CancellationToken>()).Returns(1);

        var result = await Service().GetArchitectureWeatherReportAsync("Fixture");

        result.Should().Contain("status: ok").And.Contain("| Bridge nodes | 0 |").And.Contain("100/100");
        await graph.Received(1).GetBetweennessAsync("Fixture", 10, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ChangedProjectionEvidenceDiscardsAdvisoryCommunityResults()
    {
        var stored = Publish(["callable_definitions", "callable_dependencies", "graph_projection"]);
        SetUpSlices();
        graph.FindNaturalModuleAssignmentsAsync(Arg.Any<IReadOnlyCollection<string>>(), "Fixture", Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                profiles.GetAsync("Fixture", Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(stored with
                { Profile = stored.Profile with { Analyzers = [stored.Profile.Analyzers[0] with
                    { SupportedCapabilities = ["callable_definitions", "callable_dependencies"] }] } });
                return Task.FromResult<IReadOnlyList<(CodeNode, long)>>([(Node("FindOrders"), 97), (Node("SearchOrders"), 97)]);
            });

        var result = await Service().SuggestResponsibilitySlicesAsync("Feature", "Fixture",
            includeNamespacePlan: false, includeTestPlan: false);

        result.Should().Contain("status: ok").And.Contain("Community evidence: unavailable")
            .And.Contain("status: stale").And.NotContain("community 97");
    }

    [Fact]
    public async Task ContextPackRebuildsWithoutTestsIfTheirEvidenceChanges()
    {
        const string target = "Fixture::Class::Feature";
        var stored = Publish(["callable_definitions", "callable_dependencies", "test_to_code"]);
        stored = stored with { Target = new("src/A.cs", "csharp") };
        profiles.GetAsync("Fixture", Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(stored);
        graph.GetContextForEditingAsync(target, Arg.Any<CancellationToken>())
            .Returns(new EditingContext(Node("Feature") with { Id = target }, [], [], []));
        graph.FindRelatedTestsAsync(target, "Fixture", Arg.Any<CancellationToken>()).Returns(_ =>
        {
            profiles.GetAsync("Fixture", Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(stored with
            { Profile = stored.Profile with { Analyzers = [stored.Profile.Analyzers[0] with
                { SupportedCapabilities = ["callable_definitions", "callable_dependencies"] }] } });
            return Task.FromResult<IReadOnlyList<(CodeNode, string)>>([(Node("OldTest") with { FileRole = IndexedFileRole.Test }, "direct")]);
        });

        var result = await Service().BuildMinimalContextResultAsync(target);

        result.Applicability!.Status.Should().Be("ok");
        result.IncludeTests.Should().BeFalse();
        result.Tests.Should().BeEmpty();
        result.CoverageGaps.Should().BeEmpty();
        result.SuggestedTestCommand.Should().BeNull();
        result.Degradations.Should().Contain(item => item.Step == "test_context_omitted" && item.ExceptionType.Contains("stale"));
    }

    private StoredProjectProfile Publish(string[] capabilities, bool architecture = false)
    {
        var profile = ToolCapabilityPolicyTests.Profile(capabilities: capabilities);
        var stored = new StoredProjectProfile(1, 1, DateTimeOffset.UtcNow, profile.Profile!, null)
        { AnalyzerOutcomes = profile.AnalyzerOutcomes };
        if (architecture) stored = stored with
        {
            Profile = stored.Profile with { Analyzers = [.. stored.Profile.Analyzers,
                new("configuration", true, "enabled", "unknown", ["architecture_rules"], [])] },
            AnalyzerOutcomes = [.. stored.AnalyzerOutcomes, ToolCapabilityPolicyTests.Outcome() with { Analyzer = "configuration" }]
        };
        profiles.GetAsync("Fixture", Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(stored);
        return stored;
    }

    private void SetUpSlices()
    {
        var target = Node("Feature");
        var first = Node("FindOrders");
        var second = Node("SearchOrders");
        var dependency = Node("OrderStore");
        var caller = Node("OrdersEndpoint");
        graph.QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>()).Returns(call =>
            call.Arg<CodeGraphQuery>().TypeFilter == CodeNodeType.Class ? [target] : new[] { first, second });
        graph.QueryEdgesAsync(target.Id, 1, Arg.Any<CancellationToken>()).Returns(
            [new() { SourceId = target.Id, TargetId = first.Id, Type = CodeEdgeType.Contains },
             new() { SourceId = target.Id, TargetId = second.Id, Type = CodeEdgeType.Contains }]);
        foreach (var method in new[] { first, second })
            graph.GetContextForEditingAsync(method.Id, Arg.Any<CancellationToken>())
                .Returns(new EditingContext(method, [caller], [dependency], []));
    }

    private static CodeNode Node(string name) => new()
    {
        Id = "Fixture::" + name, Name = name, Type = name == "Feature" ? CodeNodeType.Class : CodeNodeType.Method,
        FilePath = "src/A.cs", ProjectContext = "Fixture", LineNumber = 1, LineCount = 20,
        UpdatedAt = DateTimeOffset.UtcNow, SourceHash = "hash"
    };

    private CodebaseQueryService Service() => new(graph, vector, new NoOpEmbeddingProvider(), Options.Create(new CodebaseAnalysisOptions()),
        Options.Create(new CodebaseIndexingOptions()), new DefaultAnalysisProfilePolicy(),
        new StaticProjectAnalysisOptionsResolver(new CodebaseAnalysisOptions()), new ProjectProfileService(profiles));
}
