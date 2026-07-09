using CodeMeridian.Application.Services.ContextWorkflows;
using FluentAssertions;

namespace CodeMeridian.Application.Tests.Services;

public sealed class ContextWorkflowPlannerTests
{
    private readonly ContextWorkflowPlanner _sut = new();

    [Fact]
    public void Plan_WhenGoalIsMissing_ReturnsInvalidResult()
    {
        var result = _sut.Plan(new ContextWorkflowPlanRequest(
            Goal: " ",
            Target: "OrderService.PlaceOrderAsync",
            ProjectContext: "CodeMeridian",
            WorkflowType: null,
            MaxSteps: 8,
            IncludeOptionalSteps: null,
            IncludeStopConditions: true,
            IncludeExecutionHints: true));

        result.Status.Should().Be("invalid");
        result.WorkflowType.Should().Be("invalid");
        result.Error.Should().Be("Goal is required.");
        result.Steps.Should().BeEmpty();
    }

    [Fact]
    public void Plan_BeforeEditWithoutTarget_AddsDiscoveryWarningAndPrunesOptionalStepsByDefault()
    {
        var result = _sut.Plan(new ContextWorkflowPlanRequest(
            Goal: "Before editing the order service",
            Target: null,
            ProjectContext: "CodeMeridian",
            WorkflowType: "before_edit",
            MaxSteps: 12,
            IncludeOptionalSteps: null,
            IncludeStopConditions: true,
            IncludeExecutionHints: true));

        result.Status.Should().Be("valid");
        result.WorkflowType.Should().Be("before_edit");
        result.Steps.Select(step => step.Tool).Should().Equal(
            "resolve_exact_symbol",
            "check_graph_freshness",
            "get_context_for_editing",
            "find_impact",
            "find_test_shield",
            "build_minimal_context");
        result.Warnings.Should().Contain(warning =>
            warning.Contains("No target was provided", StringComparison.OrdinalIgnoreCase));
        result.Warnings.Should().Contain(warning =>
            warning.Contains("pruned by default", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Plan_BeforeEditWithFilePathTarget_UsesFileStemForSymbolAndOriginalFilePath()
    {
        const string targetPath = "src/Application/Services/ContextWorkflows/ContextWorkflowPlanner.cs";

        var result = _sut.Plan(new ContextWorkflowPlanRequest(
            Goal: "Before editing ContextWorkflowPlanner",
            Target: targetPath,
            ProjectContext: "CodeMeridian",
            WorkflowType: "before_edit",
            MaxSteps: 12,
            IncludeOptionalSteps: false,
            IncludeStopConditions: true,
            IncludeExecutionHints: true));

        var resolveStep = result.Steps.Single(step => step.Tool == "resolve_exact_symbol");
        var impactStep = result.Steps.Single(step => step.Tool == "find_impact");

        resolveStep.InputHints.Should().ContainKey("symbol").WhoseValue.Should().Be("ContextWorkflowPlanner");
        resolveStep.InputHints.Should().ContainKey("filePath").WhoseValue.Should().Be(targetPath);
        impactStep.InputHints.Should().ContainKey("nodeId").WhoseValue.Should().Be(targetPath);
    }

    [Fact]
    public void Plan_FeatureImplementationWithFeaturePath_UsesFeaturePathHint()
    {
        const string featurePath = "docs/features/43-add-context-workflow-planning.md";

        var result = _sut.Plan(new ContextWorkflowPlanRequest(
            Goal: "Implement the workflow planning feature",
            Target: featurePath,
            ProjectContext: "CodeMeridian",
            WorkflowType: "feature_implementation",
            MaxSteps: 12,
            IncludeOptionalSteps: true,
            IncludeStopConditions: true,
            IncludeExecutionHints: true));

        var featureStep = result.Steps.Single(step => step.Tool == "analyze_feature_implementation_path");

        featureStep.InputHints.Should().ContainKey("featurePath").WhoseValue.Should().Be(featurePath);
        featureStep.InputHints.Should().NotContainKey("feature");
    }

    [Fact]
    public void Plan_InferWorkflowTypeForConfigGoal_SelectsConfigurationReview()
    {
        var result = _sut.Plan(new ContextWorkflowPlanRequest(
            Goal: "Find config env var usage",
            Target: "Neo4j:Uri",
            ProjectContext: "CodeMeridian",
            WorkflowType: null,
            MaxSteps: 12,
            IncludeOptionalSteps: null,
            IncludeStopConditions: true,
            IncludeExecutionHints: true));

        result.Status.Should().Be("valid");
        result.WorkflowType.Should().Be("configuration_review");
        result.Steps.First().Tool.Should().Be("find_config_definitions");
        result.Steps[1].Tool.Should().Be("find_config_usage");
    }

    [Theory]
    [InlineData("Split the order service by responsibility", null, "responsibility_slice_planning", "resolve_exact_symbol")]
    [InlineData("Review architecture boundary drift", null, "architecture_review", "get_architectural_overview")]
    [InlineData("Replace the Neo4j client package", "Neo4j.Driver", "dependency_replacement", "replace_surface")]
    [InlineData("Investigate analyzer warning in build", null, "diagnostic_review", "find_diagnostics")]
    [InlineData("Review stale graph knowledge", null, "knowledge_health", "find_graph_drift")]
    [InlineData("Trace frontend backend route flow", null, "cross_project_trace", "find_cross_project_dependencies")]
    [InlineData("Find similar duplicate implementation shapes", null, "semantic_discovery", "find_similar_nodes")]
    [InlineData("Remember this external concept in the graph", null, "documentation_ingestion", "ingest_document")]
    [InlineData("Call project agent for repository context", null, "extension_agent_routing", "list_project_agents")]
    [InlineData("Refactor subscription sync flow", "SubscriptionService.SyncAsync", "refactor_planning", "resolve_exact_symbol")]
    [InlineData("Implement the endpoint trace feature", null, "feature_implementation", "analyze_feature_implementation_path")]
    [InlineData("Inspect this known method before editing", "SubscriptionService.SyncAsync", "before_edit", "resolve_exact_symbol")]
    public void Plan_InfersWorkflowTypesFromGoalSignals(string goal, string? target, string expectedWorkflowType, string expectedFirstTool)
    {
        var result = _sut.Plan(new ContextWorkflowPlanRequest(
            Goal: goal,
            Target: target,
            ProjectContext: "CodeMeridian",
            WorkflowType: null,
            MaxSteps: 12,
            IncludeOptionalSteps: null,
            IncludeStopConditions: true,
            IncludeExecutionHints: true));

        result.Status.Should().Be("valid");
        result.WorkflowType.Should().Be(expectedWorkflowType);
        result.Steps.First().Tool.Should().Be(expectedFirstTool);
    }

    [Fact]
    public void Plan_WithUnsupportedWorkflowType_ReturnsInvalidResultWithSupportedTypes()
    {
        var result = _sut.Plan(new ContextWorkflowPlanRequest(
            Goal: "Do something new",
            Target: "OrderService",
            ProjectContext: "CodeMeridian",
            WorkflowType: "totally_unknown",
            MaxSteps: 12,
            IncludeOptionalSteps: true,
            IncludeStopConditions: true,
            IncludeExecutionHints: true));

        result.Status.Should().Be("invalid");
        result.WorkflowType.Should().Be("invalid");
        result.Error.Should().Contain("Unsupported workflow type 'totally_unknown'");
        result.Error.Should().Contain("before_edit");
        result.SupportedWorkflowTypes.Should().Contain("documentation_ingestion");
    }

    [Fact]
    public void Plan_SemanticDiscovery_AddsEmbeddingWarningAndIncludesOptionalStepsByDefault()
    {
        var result = _sut.Plan(new ContextWorkflowPlanRequest(
            Goal: "Find similar duplicate implementation patterns",
            Target: "CodeMeridian::Method::CodeMeridian.Application.Services.CodebaseQueryService.FindImpactAsync",
            ProjectContext: "CodeMeridian",
            WorkflowType: null,
            MaxSteps: 12,
            IncludeOptionalSteps: null,
            IncludeStopConditions: true,
            IncludeExecutionHints: true));

        result.WorkflowType.Should().Be("semantic_discovery");
        result.Steps.Select(step => step.Tool).Should().ContainInOrder(
            "find_similar_nodes",
            "hybrid_search",
            "find_implementation_patterns",
            "find_duplicate_candidates");
        result.Steps.Where(step => step.RequiresEmbeddings).Should().NotBeEmpty();
        result.Warnings.Should().Contain(warning =>
            warning.Contains("embedding-dependent tools", StringComparison.OrdinalIgnoreCase));
        result.RequiresApprovalBeforeExecution.Should().BeFalse();
    }

    [Fact]
    public void Plan_DocumentationIngestion_AddsMutationWarningAndRequiresApproval()
    {
        var result = _sut.Plan(new ContextWorkflowPlanRequest(
            Goal: "Remember this architecture decision in the graph",
            Target: "docs/adr/001-routing.md",
            ProjectContext: "CodeMeridian",
            WorkflowType: "documentation_ingestion",
            MaxSteps: 12,
            IncludeOptionalSteps: true,
            IncludeStopConditions: true,
            IncludeExecutionHints: true));

        result.WorkflowType.Should().Be("documentation_ingestion");
        result.RequiresApprovalBeforeExecution.Should().BeTrue();
        result.Steps.Should().Contain(step => step.Tool == "ingest_document" && step.MutatesGraph);
        result.Warnings.Should().Contain(warning =>
            warning.Contains("graph-mutating tools", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Plan_BeforeEditWithSmallMaxSteps_TruncatesPlanAndOmitsOptionalHintsWhenDisabled()
    {
        var result = _sut.Plan(new ContextWorkflowPlanRequest(
            Goal: "Before editing ContextWorkflowPlanner",
            Target: "src/Application/Services/ContextWorkflows/ContextWorkflowPlanner.cs",
            ProjectContext: "CodeMeridian",
            WorkflowType: "before_edit",
            MaxSteps: 2,
            IncludeOptionalSteps: true,
            IncludeStopConditions: false,
            IncludeExecutionHints: false));

        result.Steps.Should().HaveCount(2);
        result.Steps.Select(step => step.StopCondition).Should().OnlyContain(value => value == null);
        result.Steps.Select(step => step.ExecutionHint).Should().OnlyContain(value => value == null);
        result.Warnings.Should().ContainSingle(warning => warning.Contains("truncated to 2 steps", StringComparison.OrdinalIgnoreCase));
    }
}
