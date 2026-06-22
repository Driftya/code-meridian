using System.Text.Json;
using CodeMeridian.Application.Services;
using CodeMeridian.Application.Services.ContextWorkflows;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;
using FluentAssertions;
using NSubstitute;

namespace CodeMeridian.Application.Tests.Services;

public sealed class CodebaseQueryServiceContextWorkflowTests
{
    [Theory]
    [InlineData("Before editing OrderService.PlaceOrderAsync", "OrderService.PlaceOrderAsync", "before_edit")]
    [InlineData("Implement the feature", "docs/features/43-add-context-workflow-planning.md", "feature_implementation")]
    [InlineData("Refactor and extract ChainService", "ChainService", "refactor_planning")]
    [InlineData("Suggest responsibility slices and namespace folders", "CodebaseQueryService", "responsibility_slice_planning")]
    [InlineData("Review architecture boundary smells", null, "architecture_review")]
    [InlineData("Replace Newtonsoft.Json with System.Text.Json", "Newtonsoft.Json", "dependency_replacement")]
    [InlineData("Check stale docs and graph drift", null, "knowledge_health")]
    [InlineData("Review TypeScript diagnostic errors", null, "diagnostic_review")]
    [InlineData("Find config env var usage", "Neo4j:Uri", "configuration_review")]
    [InlineData("Trace frontend backend API route", null, "cross_project_trace")]
    [InlineData("Find duplicate similar patterns", null, "semantic_discovery")]
    [InlineData("Ingest this document so CodeMeridian remembers it", "docs/notes.md", "documentation_ingestion")]
    [InlineData("List project agents and call relevant agent", null, "extension_agent_routing")]
    public async Task PlanContextWorkflowAsync_InfersNamedWorkflowTypes(string goal, string? target, string expectedWorkflow)
    {
        var sut = BuildService();

        using var doc = JsonDocument.Parse(await sut.PlanContextWorkflowAsync(goal, target, "CodeMeridian"));

        doc.RootElement.GetProperty("status").GetString().Should().Be("valid");
        doc.RootElement.GetProperty("workflowType").GetString().Should().Be(expectedWorkflow);
        doc.RootElement.GetProperty("steps").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task PlanContextWorkflowAsync_BeforeEdit_OrdersExactResolutionBeforeTraversal()
    {
        var sut = BuildService();

        using var doc = JsonDocument.Parse(await sut.PlanContextWorkflowAsync(
            "Before editing OrderService.PlaceOrderAsync",
            "OrderService.PlaceOrderAsync",
            workflowType: "before_edit",
            includeOptionalSteps: false));

        var tools = Tools(doc).ToArray();
        tools.Should().StartWith("resolve_exact_symbol");
        tools.Should().ContainInOrder("resolve_exact_symbol", "check_graph_freshness", "get_context_for_editing", "find_impact", "find_test_shield", "build_minimal_context");
        tools.Last().Should().Be("build_minimal_context");
        tools.Should().NotContain("find_downstream");
    }

    [Fact]
    public async Task PlanContextWorkflowAsync_BeforeEdit_DefaultPrunesOptionalSteps()
    {
        var sut = BuildService();

        using var doc = JsonDocument.Parse(await sut.PlanContextWorkflowAsync(
            "Before editing OrderService.PlaceOrderAsync",
            "OrderService.PlaceOrderAsync",
            workflowType: "before_edit"));

        var tools = Tools(doc).ToArray();
        tools.Should().Equal(
            "resolve_exact_symbol",
            "check_graph_freshness",
            "get_context_for_editing",
            "find_impact",
            "find_test_shield",
            "build_minimal_context");
        tools.Should().NotContain("find_downstream");
        tools.Should().NotContain("find_diagnostics_for_node");
        doc.RootElement.GetProperty("warnings").EnumerateArray()
            .Select(item => item.GetString())
            .Should().Contain(warning => warning!.Contains("pruned by default", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PlanContextWorkflowAsync_BeforeEdit_ExplicitOptionalStepsRestoresBroaderRecipe()
    {
        var sut = BuildService();

        using var doc = JsonDocument.Parse(await sut.PlanContextWorkflowAsync(
            "Before editing OrderService.PlaceOrderAsync",
            "OrderService.PlaceOrderAsync",
            workflowType: "before_edit",
            includeOptionalSteps: true));

        var tools = Tools(doc).ToArray();
        tools.Should().ContainInOrder("find_impact", "find_downstream", "find_test_shield", "find_diagnostics_for_node", "build_minimal_context");
        doc.RootElement.GetProperty("warnings").EnumerateArray()
            .Select(item => item.GetString())
            .Should().NotContain(warning => warning!.Contains("pruned by default", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PlanContextWorkflowAsync_FeaturePath_StartsWithFeatureImplementationAnalysis()
    {
        var sut = BuildService();

        using var doc = JsonDocument.Parse(await sut.PlanContextWorkflowAsync(
            "Implement docs/features/43-add-context-workflow-planning.md",
            "docs/features/43-add-context-workflow-planning.md"));

        doc.RootElement.GetProperty("workflowType").GetString().Should().Be("feature_implementation");
        Tools(doc).First().Should().Be("analyze_feature_implementation_path");
        Tools(doc).Should().Contain("search_documentation");
    }

    [Fact]
    public async Task PlanContextWorkflowAsync_InvalidWorkflowType_ReturnsSupportedTypes()
    {
        var sut = BuildService();

        using var doc = JsonDocument.Parse(await sut.PlanContextWorkflowAsync(
            "Do something",
            workflowType: "not_real"));

        doc.RootElement.GetProperty("status").GetString().Should().Be("invalid");
        doc.RootElement.GetProperty("error").GetString().Should().Contain("Unsupported workflow type");
        doc.RootElement.GetProperty("supportedWorkflowTypes").EnumerateArray()
            .Select(item => item.GetString())
            .Should().Contain("before_edit");
    }

    [Fact]
    public async Task PlanContextWorkflowAsync_RespectsMaxStepsAndOptionalFlag()
    {
        var sut = BuildService();

        using var doc = JsonDocument.Parse(await sut.PlanContextWorkflowAsync(
            "Review architecture boundary smells",
            workflowType: "architecture_review",
            maxSteps: 3,
            includeOptionalSteps: false));

        var tools = Tools(doc).ToArray();
        tools.Should().Equal("get_architectural_overview", "find_cycles", "find_architecture_violations");
        doc.RootElement.GetProperty("warnings").EnumerateArray()
            .Select(item => item.GetString())
            .Should().Contain(warning => warning!.Contains("truncated", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ContextWorkflowRecipes_ReferenceOnlyCatalogTools()
    {
        ContextWorkflowPlanner.MissingRecipeTools.Should().BeEmpty();
    }

    [Fact]
    public async Task ContextWorkflowDocs_ListBothWorkflowTools()
    {
        var root = FindRepositoryRoot();
        var docs = await File.ReadAllTextAsync(Path.Combine(root, "docs", "features.md"));
        var readme = await File.ReadAllTextAsync(Path.Combine(root, "README.md"));

        docs.Should().Contain("plan_context_workflow");
        docs.Should().Contain("execute_context_workflow");
        readme.Should().Contain("docs/context-workflows.md");
    }

    [Fact]
    public async Task ExecuteContextWorkflowAsync_DiagnosticWorkflow_RunsReadOnlyRequiredStep()
    {
        var graph = Substitute.For<ICodeGraphRepository>();
        var vector = Substitute.For<IVectorRepository>();
        graph.FindDiagnosticsAsync("CodeMeridian", null, Arg.Any<CancellationToken>())
            .Returns([new CodeNode
            {
                Id = "diag-1",
                Name = "warning CM0001",
                Type = CodeNodeType.Diagnostic,
                Namespace = "dotnet",
                FilePath = "src/App.cs",
                LineNumber = 12,
                Summary = "Example warning"
            }]);
        var sut = new CodebaseQueryService(graph, vector);

        using var doc = JsonDocument.Parse(await sut.ExecuteContextWorkflowAsync(
            "Review diagnostics",
            projectContext: "CodeMeridian",
            workflowType: "diagnostic_review",
            maxSteps: 1,
            includeOptionalSteps: false));

        doc.RootElement.GetProperty("status").GetString().Should().Be("completed");
        var step = doc.RootElement.GetProperty("steps")[0];
        step.GetProperty("tool").GetString().Should().Be("find_diagnostics");
        step.GetProperty("status").GetString().Should().Be("completed");
        step.GetProperty("output").GetString().Should().Contain("Example warning");
        await graph.Received(1).FindDiagnosticsAsync("CodeMeridian", null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteContextWorkflowAsync_DiagnosticWorkflow_DefaultPrunesOptionalSteps()
    {
        var graph = Substitute.For<ICodeGraphRepository>();
        var vector = Substitute.For<IVectorRepository>();
        graph.FindDiagnosticsAsync("CodeMeridian", null, Arg.Any<CancellationToken>())
            .Returns([new CodeNode
            {
                Id = "diag-1",
                Name = "warning CM0001",
                Type = CodeNodeType.Diagnostic,
                Namespace = "dotnet",
                FilePath = "src/App.cs",
                LineNumber = 12,
                Summary = "Example warning"
            }]);
        var sut = new CodebaseQueryService(graph, vector);

        using var doc = JsonDocument.Parse(await sut.ExecuteContextWorkflowAsync(
            "Review diagnostics",
            projectContext: "CodeMeridian",
            workflowType: "diagnostic_review",
            maxSteps: 4));

        var steps = doc.RootElement.GetProperty("steps").EnumerateArray().ToArray();
        doc.RootElement.GetProperty("status").GetString().Should().Be("stopped");
        steps.Select(step => step.GetProperty("tool").GetString()).Should().Equal("find_diagnostics", "build_minimal_context");
        steps[1].GetProperty("status").GetString().Should().Be("missing_input");
        doc.RootElement.GetProperty("warnings").EnumerateArray()
            .Select(item => item.GetString())
            .Should().Contain(warning => warning!.Contains("pruned by default", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExecuteContextWorkflowAsync_BeforeEditWithoutTarget_StopsOnMissingInput()
    {
        var sut = BuildService();

        using var doc = JsonDocument.Parse(await sut.ExecuteContextWorkflowAsync(
            "Before editing the order service",
            workflowType: "before_edit",
            includeOptionalSteps: false));

        doc.RootElement.GetProperty("status").GetString().Should().Be("stopped");
        var step = doc.RootElement.GetProperty("steps")[0];
        step.GetProperty("tool").GetString().Should().Be("resolve_exact_symbol");
        step.GetProperty("status").GetString().Should().Be("missing_input");
    }

    [Fact]
    public async Task ExecuteContextWorkflowAsync_BeforeEditWithTarget_RunsPlannedReadOnlySequenceInOrder()
    {
        var graph = Substitute.For<ICodeGraphRepository>();
        var vector = Substitute.For<IVectorRepository>();
        var target = new CodeNode
        {
            Id = "Method:Shop.Orders.OrderService.PlaceOrder",
            Name = "PlaceOrder",
            Type = CodeNodeType.Method,
            FilePath = "src/Orders/OrderService.cs",
            LineNumber = 12,
            LineCount = 24,
            ProjectContext = "Shop",
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var directTest = new CodeNode
        {
            Id = "t1",
            Name = "OrderServiceTests",
            Type = CodeNodeType.Class,
            FilePath = "tests/Orders/OrderServiceTests.cs",
            LineNumber = 5,
            ProjectContext = "Shop"
        };

        graph.QueryNodesAsync(
                Arg.Any<CodeGraphQuery>(),
                Arg.Any<CancellationToken>())
            .Returns([target]);
        graph.GetContextForEditingAsync(target.Id, Arg.Any<CancellationToken>())
            .Returns(new EditingContext(target, [], [], []));
        graph.FindImpactAsync(target.Id, 5, Arg.Any<CancellationToken>())
            .Returns([]);
        graph.FindImpactAsync(target.Id, 2, Arg.Any<CancellationToken>())
            .Returns([]);
        graph.FindDownstreamAsync(target.Id, 2, Arg.Any<CancellationToken>())
            .Returns([]);
        graph.FindCoverageGapsAsync("Shop", Arg.Any<CancellationToken>())
            .Returns([]);
        graph.FindRelatedTestsAsync(target.Id, "Shop", Arg.Any<CancellationToken>())
            .Returns([(directTest, "direct")]);

        var sut = new CodebaseQueryService(graph, vector);

        using var doc = JsonDocument.Parse(await sut.ExecuteContextWorkflowAsync(
            "Before editing OrderService.PlaceOrderAsync",
            target: target.Id,
            projectContext: "Shop",
            workflowType: "before_edit",
            includeOptionalSteps: false));

        doc.RootElement.GetProperty("status").GetString().Should().Be("completed");
        var steps = doc.RootElement.GetProperty("steps").EnumerateArray().ToArray();
        steps.Select(step => step.GetProperty("tool").GetString()).Should().Equal(
            "resolve_exact_symbol",
            "check_graph_freshness",
            "get_context_for_editing",
            "find_impact",
            "find_test_shield",
            "build_minimal_context");
        steps.Should().OnlyContain(step => step.GetProperty("status").GetString() == "completed");
        steps[0].GetProperty("output").GetString().Should().Contain("## Exact Symbol Resolution");
        steps[4].GetProperty("output").GetString().Should().Contain("## Test Shield Map");
        steps[5].GetProperty("output").GetString().Should().Contain("## Minimal Context Pack");
    }

    [Fact]
    public async Task ExecuteContextWorkflowAsync_BeforeEdit_ExecutesTheSameRequiredReadOnlyStepsAsThePlanner()
    {
        var graph = Substitute.For<ICodeGraphRepository>();
        var vector = Substitute.For<IVectorRepository>();
        var target = new CodeNode
        {
            Id = "Method:Shop.Orders.OrderService.PlaceOrder",
            Name = "PlaceOrder",
            Type = CodeNodeType.Method,
            FilePath = "src/Orders/OrderService.cs",
            LineNumber = 12,
            LineCount = 24,
            ProjectContext = "Shop",
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var directTest = new CodeNode
        {
            Id = "t1",
            Name = "OrderServiceTests",
            Type = CodeNodeType.Class,
            FilePath = "tests/Orders/OrderServiceTests.cs",
            LineNumber = 5,
            ProjectContext = "Shop"
        };

        graph.QueryNodesAsync(
                Arg.Any<CodeGraphQuery>(),
                Arg.Any<CancellationToken>())
            .Returns([target]);
        graph.GetContextForEditingAsync(target.Id, Arg.Any<CancellationToken>())
            .Returns(new EditingContext(target, [], [], []));
        graph.FindImpactAsync(target.Id, 5, Arg.Any<CancellationToken>())
            .Returns([]);
        graph.FindImpactAsync(target.Id, 2, Arg.Any<CancellationToken>())
            .Returns([]);
        graph.FindCoverageGapsAsync("Shop", Arg.Any<CancellationToken>())
            .Returns([]);
        graph.FindRelatedTestsAsync(target.Id, "Shop", Arg.Any<CancellationToken>())
            .Returns([(directTest, "direct")]);

        var sut = new CodebaseQueryService(graph, vector);

        using var planned = JsonDocument.Parse(await sut.PlanContextWorkflowAsync(
            "Before editing OrderService.PlaceOrderAsync",
            target: target.Id,
            projectContext: "Shop",
            workflowType: "before_edit",
            includeOptionalSteps: false));
        using var executed = JsonDocument.Parse(await sut.ExecuteContextWorkflowAsync(
            "Before editing OrderService.PlaceOrderAsync",
            target: target.Id,
            projectContext: "Shop",
            workflowType: "before_edit",
            includeOptionalSteps: false));

        var plannedTools = Tools(planned).ToArray();
        var executedSteps = executed.RootElement.GetProperty("steps").EnumerateArray().ToArray();
        var executedTools = executedSteps.Select(step => step.GetProperty("tool").GetString()).ToArray();

        plannedTools.Should().Equal(executedTools);
        executedSteps.Should().OnlyContain(step => step.GetProperty("status").GetString() == "completed");
    }

    [Fact]
    public async Task ExecuteContextWorkflowAsync_DocumentationIngestionWithoutApproval_RefusesMutation()
    {
        var sut = BuildService();

        using var doc = JsonDocument.Parse(await sut.ExecuteContextWorkflowAsync(
            "Ingest this document",
            target: "docs/notes.md",
            workflowType: "documentation_ingestion"));

        doc.RootElement.GetProperty("status").GetString().Should().Be("approval_required");
        doc.RootElement.GetProperty("error").GetString().Should().Contain("Graph mutation requires explicit approval");
        doc.RootElement.GetProperty("steps")[0].GetProperty("tool").GetString().Should().Be("ingest_document");
    }

    private static CodebaseQueryService BuildService() =>
        new(Substitute.For<ICodeGraphRepository>(), Substitute.For<IVectorRepository>());

    private static IEnumerable<string> Tools(JsonDocument doc) =>
        doc.RootElement.GetProperty("steps").EnumerateArray().Select(step => step.GetProperty("tool").GetString()!);

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CodeMeridian.sln")))
            directory = directory.Parent;

        directory.Should().NotBeNull("the test must run from inside the repository checkout");
        return directory!.FullName;
    }
}
