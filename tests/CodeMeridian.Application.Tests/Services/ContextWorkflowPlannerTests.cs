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
}
