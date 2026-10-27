using System.Text.Json;
using CodeMeridian.Application.Projects;
using CodeMeridian.Application.Services;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;
using CodeMeridian.Core.Projects;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace CodeMeridian.Application.Tests.Services;

public sealed class DirectCapabilityGuardTests
{
    private const string Target = "Fixture::Class::Example.A";
    private readonly IProjectProfileRepository profiles = Substitute.For<IProjectProfileRepository>();
    private readonly ICodeGraphRepository graph = Substitute.For<ICodeGraphRepository>();
    private readonly IVectorRepository vector = Substitute.For<IVectorRepository>();

    [Theory]
    [InlineData("unknown")]
    [InlineData("partial")]
    [InlineData("failed")]
    public async Task DirectImpactNeverRunsWithoutCompleteEvidence(string state)
    {
        var stored = Stored();
        profiles.GetAsync("Fixture", Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(stored with
        {
            AnalyzerOutcomes = [ToolCapabilityPolicyTests.Outcome() with { State = state == "unknown" ? "succeeded" : state,
                Coverage = state == "partial" ? "partial" : "unknown" }]
        });
        Node();

        var result = await Service().FindImpactResultAsync(Target);

        result.Applicability!.Status.Should().Be(state);
        result.Findings.Should().BeEmpty();
        result.ToMarkdown(ContextDetailLevel.Compact, false).Should().Contain("Tool applicability status:").And.NotContain("No callers found");
        await graph.DidNotReceiveWithAnyArgs().FindImpactAsync(default!, default, default);
    }

    [Fact]
    public async Task ValidEmptyImpactIsAnExecutedResultWithCoverage()
    {
        profiles.GetAsync("Fixture", Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(Stored());
        Node();

        var result = await Service().FindImpactResultAsync(Target);

        result.Applicability!.Status.Should().Be("ok");
        result.Applicability.Coverage.Should().Be("complete");
        result.Applicability.ProfileGeneration.Should().Be(1);
        result.Findings.Should().BeEmpty();
        result.ToMarkdown(ContextDetailLevel.Compact, false).Should().Contain("status: ok");
        await graph.Received(1).FindImpactAsync(Target, 5, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ChangedGenerationDiscardsACompletedAnalysis()
    {
        var stored = Stored();
        profiles.GetAsync("Fixture", Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(stored);
        Node();
        graph.FindImpactAsync(Target, 5, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            profiles.GetAsync("Fixture", Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(stored with { Generation = 2, LatestGeneration = 2 });
            return Task.FromResult<IReadOnlyList<(CodeNode, int)>>([(new CodeNode { Id = "caller", Name = "Caller", Type = CodeNodeType.Method }, 1)]);
        });

        var result = await Service().FindImpactResultAsync(Target);

        result.Applicability!.Status.Should().Be("stale");
        result.Findings.Should().BeEmpty();
    }

    [Fact]
    public async Task MixedProjectCannotApplyCallableAnalysisToAnObservedDocument()
    {
        var stored = Stored();
        profiles.GetAsync("Fixture", Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(stored with { Target = new("src/A.cs", "markdown") });
        Node();

        var result = await Service().FindImpactResultAsync(Target);

        result.Applicability!.Status.Should().Be("not_applicable");
        await graph.DidNotReceiveWithAnyArgs().FindImpactAsync(default!, default, default);
    }

    [Fact]
    public async Task UnspecifiedProjectDoesNotSelectAnotherProjectsCapabilities()
    {
        var result = await Service().FindArchitectureViolationsAsync();
        result.Should().Contain("status: unknown").And.NotContain("No architecture violations");
        await profiles.DidNotReceiveWithAnyArgs().GetAsync(default!, default, default);
        await graph.DidNotReceiveWithAnyArgs().FindArchitectureViolationsAsync(default, default);
    }

    [Fact]
    public async Task InventoryClassifiesUnfamiliarExtensionsBeforePlanningCodeEdits()
    {
        var stored = Stored();
        profiles.GetAsync("Fixture", "script.rb", Arg.Any<CancellationToken>()).Returns(stored with { Target = new("script.rb", "other") });

        using var plan = JsonDocument.Parse(await Service().PlanContextWorkflowAsync("Edit script", "script.rb", "Fixture", "before_edit"));

        plan.RootElement.GetProperty("status").GetString().Should().Be("blocked");
        plan.RootElement.GetProperty("applicability").GetProperty("status").GetString().Should().Be("not_applicable");
    }

    [Fact]
    public async Task ConfigurationDefinitionCallsAndPlansUseTheSameGate()
    {
        var profile = ToolCapabilityPolicyTests.Profile("configuration", "configuration", ["configuration_definitions"]);
        var stored = new StoredProjectProfile(1, 1, DateTimeOffset.UtcNow, profile.Profile!, null) { AnalyzerOutcomes = profile.AnalyzerOutcomes };
        profiles.GetAsync("Fixture", Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(stored);

        (await Service().FindConfigDefinitionsAsync("Feature:Enabled", "Fixture")).Should().Contain("status: ok");
        var plan = new CodeMeridian.Application.Services.ContextWorkflows.ContextWorkflowPlanner().Plan(
            new("Review config", "Feature:Enabled", "Fixture", "configuration_review"), profile with { Target = null });
        plan.Steps.Single(step => step.Tool == "find_config_definitions").Applicability!.Status.Should().Be("ready");
        plan.Status.Should().Be("blocked"); // Definitions alone never establish usage coverage.
    }

    [Fact]
    public async Task EvidenceAcquisitionFailureAndCancellationAreDistinct()
    {
        profiles.GetAsync("Fixture", Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(Task.FromException<StoredProjectProfile?>(new InvalidOperationException("backend unavailable")));
        (await Service().FindConfigDefinitionsAsync("Key", "Fixture")).Should().Contain("status: failed");
        await graph.DidNotReceiveWithAnyArgs().FindConfigDefinitionsAsync(default!, default, default);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var action = () => Service().FindConfigDefinitionsAsync("Key", "Fixture", cancellation.Token);
        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task GdsFailureIsNotAnExecutedEmptyResult()
    {
        var profile = ToolCapabilityPolicyTests.Profile(capabilities: ["callable_definitions", "callable_dependencies", "graph_projection"]);
        profiles.GetAsync("Fixture", Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(Stored() with { Profile = profile.Profile! });
        graph.GetPageRankAsync("Fixture", 20, Arg.Any<CancellationToken>()).Returns(
            Task.FromException<IReadOnlyList<(CodeNode, double)>>(new InvalidOperationException("GDS unavailable")));

        var result = await Service().GetPageRankAsync("Fixture");

        result.Should().Contain("status: failed").And.NotContain("status: ok");
    }

    [Fact]
    public async Task ContextPackOmitsUnauditedTestSections()
    {
        profiles.GetAsync("Fixture", Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(Stored());
        Node();

        var result = await Service().BuildMinimalContextResultAsync(Target);

        result.Applicability!.Status.Should().Be("ok");
        result.IncludeTests.Should().BeFalse();
        result.Degradations.Should().Contain(item => item.Step == "test_context_omitted");
        await graph.DidNotReceiveWithAnyArgs().FindRelatedTestsAsync(default!, default, default);
    }

    [Fact]
    public async Task StaleOptionalFirstStepCannotMakeAnUnexecutedRequiredWorkflowLookCompleted()
    {
        var profile = ToolCapabilityPolicyTests.Profile(capabilities: ["callable_definitions", "callable_dependencies", "semantic_embeddings"]);
        var stored = Stored() with { Profile = profile.Profile! };
        profiles.GetAsync("Fixture", Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(stored, stored with { Generation = 2, LatestGeneration = 2 });

        using var result = JsonDocument.Parse(await Service().ExecuteContextWorkflowAsync("Find similar code", projectContext: "Fixture", workflowType: "semantic_discovery"));

        result.RootElement.GetProperty("status").GetString().Should().Be("blocked");
        result.RootElement.GetProperty("steps")[0].GetProperty("required").GetBoolean().Should().BeFalse();
        result.RootElement.GetProperty("steps")[0].GetProperty("status").GetString().Should().Be("stale");
    }

    [Fact]
    public async Task ImpactDiscardsCallersFromAnUnverifiedProject()
    {
        profiles.GetAsync("Fixture", Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(Stored());
        Node();
        graph.FindImpactAsync(Target, 5, Arg.Any<CancellationToken>()).Returns(
            [(new CodeNode { Id = "Other::Class::Caller", Name = "Caller", Type = CodeNodeType.Class, ProjectContext = "Other" }, 1)]);

        var result = await Service().FindImpactResultAsync(Target);

        result.Applicability!.Status.Should().Be("unknown");
        result.Findings.Should().BeEmpty();
        result.ToMarkdown(ContextDetailLevel.Compact, false).Should().NotContain("No callers found");
    }

    [Fact]
    public async Task AnchoredHybridSearchUsesTheVerifiedProjectWhenNoneIsSupplied()
    {
        var profile = ToolCapabilityPolicyTests.Profile(capabilities: ["callable_definitions", "callable_dependencies", "semantic_embeddings"]);
        profiles.GetAsync("Fixture", Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(Stored() with { Profile = profile.Profile! });
        Node();
        var embeddings = Substitute.For<IEmbeddingProvider>();
        embeddings.IsAvailableAsync(Arg.Any<CancellationToken>()).Returns(true);
        embeddings.GenerateEmbeddingAsync("query", Arg.Any<CancellationToken>()).Returns([0.1f, 0.2f]);

        var result = await Service(embeddings).FindHybridSearchAsync("query", nearNodeId: Target);

        result.Should().Contain("status: ok");
        await graph.Received(1).FindHybridMatchesAsync(Arg.Any<float[]>(), Target, 3, "Fixture", true, 10, Arg.Any<CancellationToken>());
        await graph.DidNotReceive().FindHybridMatchesAsync(Arg.Any<float[]>(), Arg.Any<string?>(), Arg.Any<int>(), null,
            Arg.Any<bool>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NodeConnectionRequiresBothTargetsAndRejectsUnverifiedCrossProjectPaths()
    {
        profiles.GetAsync("Fixture", Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(Stored());
        Node();

        var result = await Service().FindConnectionResultAsync(Target, "Other::Class::B");

        result.Applicability!.Status.Should().Be("unknown");
        result.Nodes.Should().BeEmpty();
        await graph.DidNotReceiveWithAnyArgs().FindConnectionAsync(default!, default!, default);
    }

    [Theory]
    [InlineData("csharp", "csharp")]
    [InlineData("typescript", "typescript")]
    public async Task VerifiedCallableWorkflowPreservesRequiredOrderingAndExecutes(string kind, string analyzer)
    {
        var profile = ToolCapabilityPolicyTests.Profile(kind, analyzer, ["callable_definitions", "callable_dependencies", "test_to_code"]);
        var stored = new StoredProjectProfile(1, 1, DateTimeOffset.UtcNow, profile.Profile!, new("src/A.cs", kind)) { AnalyzerOutcomes = profile.AnalyzerOutcomes };
        profiles.GetAsync("Fixture", Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(stored);
        Node();
        graph.QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>()).Returns(
            [new CodeNode { Id = Target, Name = "A", Type = CodeNodeType.Class, FilePath = "src/A.cs", ProjectContext = "Fixture", LineCount = 20 }]);

        using var result = JsonDocument.Parse(await Service().ExecuteContextWorkflowAsync("Edit A", "src/A.cs", "Fixture", "before_edit"));

        result.RootElement.GetProperty("status").GetString().Should().Be("completed");
        var steps = result.RootElement.GetProperty("steps").EnumerateArray().ToArray();
        steps.Select(step => step.GetProperty("tool").GetString()).Should().Equal(
            "resolve_exact_symbol", "check_graph_freshness", "get_context_for_editing", "find_impact", "find_test_shield", "build_minimal_context");
        steps.Should().OnlyContain(step => step.GetProperty("status").GetString() == "completed");
    }

    private void Node() => graph.GetContextForEditingAsync(Target, Arg.Any<CancellationToken>()).Returns(new EditingContext(
        new CodeNode { Id = Target, Name = "A", Type = CodeNodeType.Class, FilePath = "src/A.cs", ProjectContext = "Fixture" }, [], [], []));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FrontendSingleColonIdentityUsesVerifiedInventoryRatherThanPathParsing(bool wrongNode)
    {
        const string target = "Fixture:File:index.html";
        var profile = ToolCapabilityPolicyTests.Profile("html", "frontend", ["frontend_relationships"]);
        profiles.GetAsync("Fixture", Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(
            new StoredProjectProfile(1, 1, DateTimeOffset.UtcNow, profile.Profile!, new("index.html", "html")) { AnalyzerOutcomes = profile.AnalyzerOutcomes });
        graph.GetContextForEditingAsync(target, Arg.Any<CancellationToken>()).Returns(new EditingContext(new CodeNode
        {
            Id = wrongNode ? "Fixture:File:other.html" : target, Name = "index.html", Type = CodeNodeType.File,
            FilePath = "index.html", ProjectContext = "Fixture"
        }, [], [], []));
        var result = await Service().FindConnectionResultAsync(target, target);
        result.Applicability!.Status.Should().Be(wrongNode ? "unknown" : "ok");
        if (wrongNode) await graph.DidNotReceiveWithAnyArgs().FindConnectionAsync(default!, default!, default);
        else await graph.Received(1).FindConnectionAsync(target, target, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishedPowerShellMethodsCannotBorrowCallableReadinessFromCSharp()
    {
        profiles.GetAsync("Fixture", Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(Stored() with { Target = new("main.ps1", "powershell") });
        Node();
        var result = await Service().FindImpactResultAsync(Target);
        result.Applicability!.Status.Should().Be("unsupported");
        await graph.DidNotReceiveWithAnyArgs().FindImpactAsync(default!, default, default);
    }

    private static StoredProjectProfile Stored()
    {
        var profile = ToolCapabilityPolicyTests.Profile();
        return new(1, 1, DateTimeOffset.UtcNow, profile.Profile!, new("src/A.cs", "csharp")) { AnalyzerOutcomes = profile.AnalyzerOutcomes };
    }

    private CodebaseQueryService Service(IEmbeddingProvider? embeddings = null) => new(graph, vector, embeddings ?? new NoOpEmbeddingProvider(), Options.Create(new CodebaseAnalysisOptions()),
        Options.Create(new CodebaseIndexingOptions()), new DefaultAnalysisProfilePolicy(),
        new StaticProjectAnalysisOptionsResolver(new CodebaseAnalysisOptions()), new ProjectProfileService(profiles));
}
