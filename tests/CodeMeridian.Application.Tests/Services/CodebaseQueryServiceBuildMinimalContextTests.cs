using CodeMeridian.Application.Services;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace CodeMeridian.Application.Tests.Services;

public sealed class CodebaseQueryServiceBuildMinimalContextTests : CodebaseQueryServiceAnalyticsTestBase
{
    [Fact]
    public async Task BuildMinimalContextAsync_WithRelatedTests_RendersDirectAndHeuristicSections()
    {
        var (sut, graph) = Build(WithDotNetTestCommands());
        var target = Node("m1", "PlaceOrder", CodeNodeType.Method, "src/Orders/OrderService.cs", 12, "Shop", lineCount: 24);
        var directTest = Node("t1", "OrderServiceTests", CodeNodeType.Class, "tests/Orders/OrderServiceTests.cs", 20, "Shop");
        var heuristicTest = Node("t2", "PlaceOrderTests", CodeNodeType.Class, "tests/Orders/PlaceOrderTests.cs", 8, "Shop");

        graph.GetContextForEditingAsync("Method:Shop.Orders.OrderService.PlaceOrder", Arg.Any<CancellationToken>())
             .Returns(new EditingContext(target, [], [], []));
        graph.FindImpactAsync(target.Id, 2, Arg.Any<CancellationToken>())
             .Returns([]);
        graph.FindDownstreamAsync(target.Id, 2, Arg.Any<CancellationToken>())
             .Returns([]);
        graph.FindCoverageGapsAsync("Shop", Arg.Any<CancellationToken>())
             .Returns([]);
        graph.FindRelatedTestsAsync(target.Id, "Shop", Arg.Any<CancellationToken>())
             .Returns([(directTest, "direct"), (heuristicTest, "heuristic")]);

        var result = await sut.BuildMinimalContextAsync(target: "Method:Shop.Orders.OrderService.PlaceOrder");

        result.Should().Contain("### Relevant tests (2)");
        result.Should().Contain("Direct regression tests:");
        result.Should().Contain("OrderServiceTests");
        result.Should().Contain("Heuristic shield tests:");
        result.Should().Contain("PlaceOrderTests");
        result.Should().Contain("heuristic match near `PlaceOrder`");
        result.Should().Contain("Suggested command: `dotnet test --filter FullyQualifiedName~OrderServiceTests`");
        result.Should().Contain("**Estimated:**");
        result.Should().Contain("**Complexity:** Low");
        result.Should().Contain("Small or fast model likely sufficient");
    }

    [Fact]
    public async Task BuildMinimalContextAsync_IgnoresNonTestRelatedMatches()
    {
        var (sut, graph) = Build();
        var target = Node("m3", "PlaceOrder", CodeNodeType.Method, "src/Orders/OrderService.cs", 12, "Shop", lineCount: 24);
        var sourceHelper = Node("h2", "AppendFocusedVerificationPlan", CodeNodeType.Method, "src/Application/Helpers/TestFormatting.cs", 30, "Shop", fileRole: IndexedFileRole.Source);
        var realTest = Node("t4", "PlaceOrderTests", CodeNodeType.Class, "tests/Orders/PlaceOrderTests.cs", 18, "Shop", fileRole: IndexedFileRole.Test);

        graph.GetContextForEditingAsync(target.Id, Arg.Any<CancellationToken>())
             .Returns(new EditingContext(target, [], [], []));
        graph.FindImpactAsync(target.Id, 2, Arg.Any<CancellationToken>())
             .Returns([]);
        graph.FindDownstreamAsync(target.Id, 2, Arg.Any<CancellationToken>())
             .Returns([]);
        graph.FindCoverageGapsAsync("Shop", Arg.Any<CancellationToken>())
             .Returns([]);
        graph.FindRelatedTestsAsync(target.Id, "Shop", Arg.Any<CancellationToken>())
             .Returns([(sourceHelper, "heuristic"), (realTest, "direct")]);

        var result = await sut.BuildMinimalContextAsync(target.Id);

        result.Should().Contain("PlaceOrderTests");
        result.Should().NotContain("AppendFocusedVerificationPlan");
    }

    [Fact]
    public async Task BuildMinimalContextAsync_UsesSameFocusedVerificationTestSetAsFindTestShield()
    {
        var (sut, graph) = Build(WithDotNetTestCommands());
        var target = Node("e1", "POST /api/orders", CodeNodeType.ApiEndpoint, "src/Api/OrdersEndpoint.cs", 12, "Shop", lineCount: 24);
        var directTest = Node("t1", "OrdersEndpointTests", CodeNodeType.Class, "tests/Api/OrdersEndpointTests.cs", 5, "Shop");
        var heuristicTest = Node("t2", "OrdersEndpointWorkflowTests", CodeNodeType.Class, "tests/Api/OrdersEndpointWorkflowTests.cs", 11, "Shop");

        graph.GetContextForEditingAsync(target.Id, Arg.Any<CancellationToken>())
             .Returns(new EditingContext(target, [], [], []));
        graph.FindImpactAsync(target.Id, 2, Arg.Any<CancellationToken>())
             .Returns([]);
        graph.FindDownstreamAsync(target.Id, 2, Arg.Any<CancellationToken>())
             .Returns([]);
        graph.FindCoverageGapsAsync("Shop", Arg.Any<CancellationToken>())
             .Returns([]);
        graph.FindRelatedTestsAsync(target.Id, "Shop", Arg.Any<CancellationToken>())
             .Returns([(directTest, "direct"), (heuristicTest, "heuristic")]);

        var shield = await sut.FindTestShieldAsync(target.Id, depth: 2);
        var context = await sut.BuildMinimalContextAsync(target.Id);

        shield.Should().Contain("Direct regression tests:");
        shield.Should().Contain("Heuristic shield tests:");
        context.Should().Contain("Contract/API forwarding tests:");
        context.Should().Contain("Heuristic shield tests:");
        shield.Should().Contain("OrdersEndpointTests");
        shield.Should().Contain("OrdersEndpointWorkflowTests");
        context.Should().Contain("OrdersEndpointTests");
        context.Should().Contain("OrdersEndpointWorkflowTests");
        shield.Should().Contain("`dotnet test --filter FullyQualifiedName~OrdersEndpointTests`");
        context.Should().Contain("`dotnet test --filter FullyQualifiedName~OrdersEndpointTests`");
    }

    [Fact]
    public async Task BuildMinimalContextAsync_PreservesFindTestShieldVerificationStoryForMethodTargets()
    {
        var (sut, graph) = Build(WithDotNetTestCommands());
        var target = Node("m1", "PlaceOrder", CodeNodeType.Method, "src/Orders/OrderService.cs", 12, "Shop", lineCount: 24);
        var directTest = Node("t1", "OrderServiceTests", CodeNodeType.Class, "tests/Orders/OrderServiceTests.cs", 20, "Shop");
        var heuristicTest = Node("t2", "PlaceOrderTests", CodeNodeType.Class, "tests/Orders/PlaceOrderTests.cs", 8, "Shop");

        graph.GetContextForEditingAsync(target.Id, Arg.Any<CancellationToken>())
             .Returns(new EditingContext(target, [], [], []));
        graph.FindImpactAsync(target.Id, 2, Arg.Any<CancellationToken>())
             .Returns([]);
        graph.FindDownstreamAsync(target.Id, 2, Arg.Any<CancellationToken>())
             .Returns([]);
        graph.FindCoverageGapsAsync("Shop", Arg.Any<CancellationToken>())
             .Returns([]);
        graph.FindRelatedTestsAsync(target.Id, "Shop", Arg.Any<CancellationToken>())
             .Returns([(directTest, "direct"), (heuristicTest, "heuristic")]);

        var shield = await sut.FindTestShieldAsync(target.Id, depth: 2);
        var context = await sut.BuildMinimalContextAsync(target.Id);

        shield.Should().Contain("### Focused verification plan (2)");
        shield.Should().Contain("Direct regression tests");
        shield.Should().Contain("Heuristic shield tests");
        context.Should().Contain("### Relevant tests (2)");
        context.Should().Contain("Direct regression tests:");
        context.Should().Contain("Heuristic shield tests:");
        shield.Should().Contain("OrderServiceTests");
        shield.Should().Contain("PlaceOrderTests");
        context.Should().Contain("OrderServiceTests");
        context.Should().Contain("PlaceOrderTests");
        shield.Should().Contain("heuristic");
        context.Should().Contain("heuristic match near `PlaceOrder`");
        shield.Should().Contain("`dotnet test --filter FullyQualifiedName~OrderServiceTests`");
        context.Should().Contain("`dotnet test --filter FullyQualifiedName~OrderServiceTests`");
    }

    [Fact]
    public async Task BuildMinimalContextAsync_WhenTestsDisabled_OmitsTestSections()
    {
        var (sut, graph) = Build();
        var target = Node("m1", "PlaceOrder", CodeNodeType.Method, "src/Orders/OrderService.cs", 12, "Shop", lineCount: 24);

        graph.GetContextForEditingAsync("Method:Shop.Orders.OrderService.PlaceOrder", Arg.Any<CancellationToken>())
             .Returns(new EditingContext(target, [], [], []));
        graph.FindImpactAsync(target.Id, 2, Arg.Any<CancellationToken>())
             .Returns([]);
        graph.FindDownstreamAsync(target.Id, 2, Arg.Any<CancellationToken>())
             .Returns([]);

        var result = await sut.BuildMinimalContextAsync(
            target: "Method:Shop.Orders.OrderService.PlaceOrder",
            includeTests: false);

        result.Should().NotContain("Relevant coverage gaps");
        result.Should().NotContain("Relevant tests");
    }

    [Fact]
    public async Task BuildMinimalContextAsync_WithSourceSnippets_IncludesBudgetedTargetSnippet()
    {
        var (sut, graph) = Build();
        var target = Node(
            "m1",
            "PlaceOrder",
            CodeNodeType.Method,
            "src/Orders/OrderService.cs",
            4,
            "Shop",
            lineCount: 5,
            sourceSnippet:
            """
                public void PlaceOrder()
                {
                    ValidateOrder();
                    SaveOrder();
                }
            """);

        graph.GetContextForEditingAsync("Method:Shop.Orders.OrderService.PlaceOrder", Arg.Any<CancellationToken>())
             .Returns(new EditingContext(target, [], [], []));
        graph.FindImpactAsync(target.Id, 2, Arg.Any<CancellationToken>())
             .Returns([]);
        graph.FindDownstreamAsync(target.Id, 2, Arg.Any<CancellationToken>())
             .Returns([]);
        graph.FindCoverageGapsAsync("Shop", Arg.Any<CancellationToken>())
             .Returns([]);
        graph.FindRelatedTestsAsync(target.Id, "Shop", Arg.Any<CancellationToken>())
             .Returns([]);

        var result = await sut.BuildMinimalContextAsync(
            target: "Method:Shop.Orders.OrderService.PlaceOrder",
            maxTokens: 800,
            includeSourceSnippets: true);

        result.Should().Contain("### Source snippets");
        result.Should().Contain("PlaceOrder");
        result.Should().Contain("ValidateOrder();");
        result.Should().NotContain("source extraction is not implemented");
    }

    [Fact]
    public async Task BuildMinimalContextAsync_WithSourceSnippetsAndNoRemainingBudget_SkipsSnippets()
    {
        var (sut, graph) = Build();
        var target = Node("m1", "PlaceOrder", CodeNodeType.Method, "src/Orders/OrderService.cs", 12, "Shop", lineCount: 24);

        graph.GetContextForEditingAsync("Method:Shop.Orders.OrderService.PlaceOrder", Arg.Any<CancellationToken>())
             .Returns(new EditingContext(target, [], [], []));
        graph.FindImpactAsync(target.Id, 2, Arg.Any<CancellationToken>())
             .Returns([]);
        graph.FindDownstreamAsync(target.Id, 2, Arg.Any<CancellationToken>())
             .Returns([]);
        graph.FindCoverageGapsAsync("Shop", Arg.Any<CancellationToken>())
             .Returns([]);
        graph.FindRelatedTestsAsync(target.Id, "Shop", Arg.Any<CancellationToken>())
             .Returns([]);

        var result = await sut.BuildMinimalContextAsync(
            target: "Method:Shop.Orders.OrderService.PlaceOrder",
            maxTokens: 10,
            includeSourceSnippets: true);

        result.Should().Contain("### Source snippets");
        result.Should().Contain("Skipped: no remaining token budget");
    }

    [Fact]
    public async Task BuildMinimalContextAsync_WithLargeImpact_ReturnsLargerModelGuidance()
    {
        var (sut, graph) = Build();
        var target = new CodeNode
        {
            Id = "Method:Shop.Payments.PaymentGateway.ChargeAsync",
            Name = "ChargeAsync",
            Type = CodeNodeType.Method,
            FilePath = "src/Payments/PaymentGateway.cs",
            LineNumber = 42,
            LineCount = 360,
            Summary = "Charges a payment through an external processor.",
            ProjectContext = "Shop",
            ChangeCount = 6
        };
        var impact = Enumerable.Range(1, 30)
            .Select(i => (Node($"caller-{i}", $"Caller{i}", CodeNodeType.Method, $"src/Callers/Caller{i}.cs", project: i % 2 == 0 ? "Shop.Api" : "Shop"), 1))
            .ToArray();
        var downstream = Enumerable.Range(1, 10)
            .Select(i => (Node($"dep-{i}", $"Dependency{i}", CodeNodeType.Method, $"src/Dependencies/Dependency{i}.cs", project: "Shop"), 1))
            .ToArray();

        graph.GetContextForEditingAsync(target.Id, Arg.Any<CancellationToken>())
             .Returns(new EditingContext(target, [], [], []));
        graph.FindImpactAsync(target.Id, 2, Arg.Any<CancellationToken>())
             .Returns(impact);
        graph.FindDownstreamAsync(target.Id, 2, Arg.Any<CancellationToken>())
             .Returns(downstream);
        graph.FindCoverageGapsAsync("Shop", Arg.Any<CancellationToken>())
             .Returns([target]);
        graph.FindRelatedTestsAsync(target.Id, "Shop", Arg.Any<CancellationToken>())
             .Returns([]);

        var result = await sut.BuildMinimalContextAsync(target.Id, includeSourceSnippets: true);

        result.Should().Contain("**Complexity:** High");
        result.Should().Contain("Use a larger reasoning model or larger context window");
        result.Should().Contain("**Expansion risk:** High");
        result.Should().Contain("30 affected nodes");
        result.Should().Contain("10 downstream dependencies");
        result.Should().Contain("nearby coverage gaps");
        result.Should().Contain("no related tests found");
    }

    [Fact]
    public async Task BuildMinimalContextAsync_WithRouteLinkedGraphData_IncludesApiEndpointsAndCrossProjectCallers()
    {
        var (sut, graph) = Build();
        var target = Node(
            "Method:Shop.Api.OrdersController.CreateOrder",
            "CreateOrder",
            CodeNodeType.Method,
            "src/api/OrdersController.cs",
            18,
            "Shop.Api",
            lineCount: 20);
        var frontendCaller = Node(
            "Method:Shop.Web.OrdersClient.submitOrder",
            "submitOrder",
            CodeNodeType.Method,
            "src/web/orders-client.ts",
            27,
            "Shop.Web");
        var apiEndpoint = Node(
            "Shop.Api::ApiEndpoint::POST /api/orders",
            "POST /api/orders",
            CodeNodeType.ApiEndpoint,
            project: "Shop.Api");

        graph.GetContextForEditingAsync(target.Id, Arg.Any<CancellationToken>())
             .Returns(new EditingContext(target, [apiEndpoint], [], []));
        graph.FindImpactAsync(target.Id, 2, Arg.Any<CancellationToken>())
             .Returns([(apiEndpoint, 1), (frontendCaller, 2)]);
        graph.FindDownstreamAsync(target.Id, 2, Arg.Any<CancellationToken>())
             .Returns([(apiEndpoint, 1)]);
        graph.FindCoverageGapsAsync("Shop.Api", Arg.Any<CancellationToken>())
             .Returns([]);
        graph.FindRelatedTestsAsync(target.Id, "Shop.Api", Arg.Any<CancellationToken>())
             .Returns([]);

        var result = await sut.BuildMinimalContextAsync(target.Id);

        result.Should().Contain("### Direct callers (1)");
        result.Should().Contain("**ApiEndpoint** `POST /api/orders`");
        result.Should().Contain("### Near impact (2)");
        result.Should().Contain("submitOrder");
        result.Should().Contain("src/web/orders-client.ts");
        result.Should().Contain("### Near downstream (1)");
        result.Should().Contain("POST /api/orders");
        result.Should().Contain("1 cross-project edges");
    }

    [Fact]
    public async Task BuildMinimalContextAsync_ExcludesGeneratedAndMigrationNodesFromAgentContext()
    {
        var (sut, graph) = Build();
        var target = Node("target", "OrderService.PlaceOrder", CodeNodeType.Method, "src/OrderService.cs", 10, "Shop", lineCount: 20, fileRole: IndexedFileRole.Source);

        graph.GetContextForEditingAsync(target.Id, Arg.Any<CancellationToken>())
             .Returns(new EditingContext(
                 target,
                 [
                     Node("caller", "OrdersController.Post", CodeNodeType.Method, "src/OrdersController.cs", fileRole: IndexedFileRole.Source),
                     Node("generated", "GeneratedClient.Send", CodeNodeType.Method, "src/Generated/Client.g.cs", fileRole: IndexedFileRole.Generated)
                 ],
                 [
                     Node("migration", "CreateUsers.Up", CodeNodeType.Method, "src/Migrations/CreateUsers.cs", fileRole: IndexedFileRole.Migration)
                 ],
                 []));
        graph.FindImpactAsync(target.Id, 2, Arg.Any<CancellationToken>())
             .Returns([]);
        graph.FindDownstreamAsync(target.Id, 2, Arg.Any<CancellationToken>())
             .Returns([]);
        graph.FindCoverageGapsAsync("Shop", Arg.Any<CancellationToken>())
             .Returns([]);
        graph.FindRelatedTestsAsync(target.Id, "Shop", Arg.Any<CancellationToken>())
             .Returns([]);

        var result = await sut.BuildMinimalContextAsync(target.Id);

        result.Should().Contain("OrdersController.Post");
        result.Should().NotContain("GeneratedClient.Send");
        result.Should().NotContain("CreateUsers.Up");
    }

    [Fact]
    public async Task BuildMinimalContextAsync_WithExplainPaths_ExplainsWhyFilesAreIncluded()
    {
        var (sut, graph) = Build();
        var target = Node("target", "PlaceOrder", CodeNodeType.Method, "src/Orders/OrderService.cs", 12, "Shop", lineCount: 24, fileRole: IndexedFileRole.Source);
        var caller = Node("caller", "OrdersController.Post", CodeNodeType.Method, "src/Api/OrdersController.cs", 18, "Shop", fileRole: IndexedFileRole.Source);
        var directTest = Node("test", "OrderServiceTests", CodeNodeType.Class, "tests/Orders/OrderServiceTests.cs", 7, "Shop", fileRole: IndexedFileRole.Test);
        var diagnostic = Node("diag", "CS8602", CodeNodeType.Diagnostic, "src/Api/OrdersController.cs", 20, "Shop");

        graph.GetContextForEditingAsync(target.Id, Arg.Any<CancellationToken>())
             .Returns(new EditingContext(target, [caller], [], []));
        graph.FindImpactAsync(target.Id, 2, Arg.Any<CancellationToken>())
             .Returns([(caller, 1)]);
        graph.FindDownstreamAsync(target.Id, 2, Arg.Any<CancellationToken>())
             .Returns([]);
        graph.FindCoverageGapsAsync("Shop", Arg.Any<CancellationToken>())
             .Returns([]);
        graph.FindRelatedTestsAsync(target.Id, "Shop", Arg.Any<CancellationToken>())
             .Returns([(directTest, "direct")]);
        graph.FindConnectionAsync(target.Id, caller.Id, Arg.Any<CancellationToken>())
             .Returns([
                 (target, "Calls"),
                 (caller, null)
             ]);
        graph.FindConnectionAsync(target.Id, directTest.Id, Arg.Any<CancellationToken>())
             .Returns([
                 (target, "Calls"),
                 (directTest, null)
             ]);
        graph.FindDiagnosticsForNodeAsync(target.Id, Arg.Any<CancellationToken>())
             .Returns([]);
        graph.FindDiagnosticsForNodeAsync(caller.Id, Arg.Any<CancellationToken>())
             .Returns([diagnostic]);
        graph.FindDiagnosticsForNodeAsync(directTest.Id, Arg.Any<CancellationToken>())
             .Returns([]);

        var result = await sut.BuildMinimalContextAsync(
            target.Id,
            explainPaths: true);

        result.Should().Contain("### File inclusion paths (3)");
        result.Should().Contain("`src/Orders/OrderService.cs`");
        result.Should().Contain("target file");
        result.Should().Contain("`src/Api/OrdersController.cs`");
        result.Should().Contain("direct caller");
        result.Should().Contain("path: `PlaceOrder` -[Calls]- `OrdersController.Post`");
        result.Should().Contain("nearby diagnostics: `CS8602`");
        result.Should().Contain("`tests/Orders/OrderServiceTests.cs`");
        result.Should().Contain("direct related test");
        result.Should().Contain("nearby tests: `OrderServiceTests`");
    }

    [Fact]
    public async Task BuildMinimalContextAsync_WhenExplainPathsFails_ReturnsDegradedContextPack()
    {
        var (sut, graph) = Build();
        var target = Node("target", "PlaceOrder", CodeNodeType.Method, "src/Orders/OrderService.cs", 12, "Shop", lineCount: 24, fileRole: IndexedFileRole.Source);
        var caller = Node("caller", "OrdersController.Post", CodeNodeType.Method, "src/Api/OrdersController.cs", 18, "Shop", fileRole: IndexedFileRole.Source);
        var directTest = Node("test", "OrderServiceTests", CodeNodeType.Class, "tests/Orders/OrderServiceTests.cs", 7, "Shop", fileRole: IndexedFileRole.Test);

        graph.GetContextForEditingAsync(target.Id, Arg.Any<CancellationToken>())
             .Returns(new EditingContext(target, [caller], [], []));
        graph.FindImpactAsync(target.Id, 2, Arg.Any<CancellationToken>())
             .Returns([(caller, 1)]);
        graph.FindDownstreamAsync(target.Id, 2, Arg.Any<CancellationToken>())
             .Returns([]);
        graph.FindCoverageGapsAsync("Shop", Arg.Any<CancellationToken>())
             .Returns([]);
        graph.FindRelatedTestsAsync(target.Id, "Shop", Arg.Any<CancellationToken>())
             .Returns([(directTest, "direct")]);
        graph.FindConnectionAsync(target.Id, caller.Id, Arg.Any<CancellationToken>())
             .ThrowsAsync(new KeyNotFoundException("The given key 'name' was not present in the dictionary."));
        graph.FindDiagnosticsForNodeAsync(target.Id, Arg.Any<CancellationToken>())
             .Returns([]);

        var result = await sut.BuildMinimalContextAsync(
            target.Id,
            explainPaths: true);

        result.Should().Contain("## Minimal Context Pack");
        result.Should().Contain("### Files likely needed");
        result.Should().Contain("`src/Orders/OrderService.cs`");
        result.Should().Contain("`src/Api/OrdersController.cs`");
        result.Should().Contain("`tests/Orders/OrderServiceTests.cs`");
        result.Should().Contain("### Degraded mode");
        result.Should().Contain("`context_pack_status=degraded`");
        result.Should().Contain("failed_step: `file_path_explanation`");
        result.Should().Contain("exception: `KeyNotFoundException`");
        result.Should().Contain("`resolve_exact_symbol`, `find_impact`, and `find_test_shield`");
    }

    [Fact]
    public async Task BuildMinimalContextAsync_WhenImpactLookupFails_ReturnsPartialPackAndListsFailure()
    {
        var (sut, graph) = Build(WithDotNetTestCommands());
        var target = Node("target", "PlaceOrder", CodeNodeType.Method, "src/Orders/OrderService.cs", 12, "Shop", lineCount: 24, fileRole: IndexedFileRole.Source);
        var caller = Node("caller", "OrdersController.Post", CodeNodeType.Method, "src/Api/OrdersController.cs", 18, "Shop", fileRole: IndexedFileRole.Source);
        var directTest = Node("test", "OrderServiceTests", CodeNodeType.Class, "tests/Orders/OrderServiceTests.cs", 7, "Shop", fileRole: IndexedFileRole.Test);

        graph.GetContextForEditingAsync(target.Id, Arg.Any<CancellationToken>())
             .Returns(new EditingContext(target, [caller], [], []));
        graph.FindImpactAsync(target.Id, 2, Arg.Any<CancellationToken>())
             .ThrowsAsync(new InvalidOperationException("impact failed"));
        graph.FindDownstreamAsync(target.Id, 2, Arg.Any<CancellationToken>())
             .Returns([]);
        graph.FindCoverageGapsAsync("Shop", Arg.Any<CancellationToken>())
             .Returns([]);
        graph.FindRelatedTestsAsync(target.Id, "Shop", Arg.Any<CancellationToken>())
             .Returns([(directTest, "direct")]);

        var result = await sut.BuildMinimalContextAsync(target.Id);

        result.Should().Contain("## Minimal Context Pack");
        result.Should().Contain("### Direct callers (1)");
        result.Should().Contain("OrdersController.Post");
        result.Should().Contain("### Relevant tests (1)");
        result.Should().Contain("OrderServiceTests");
        result.Should().Contain("Direct regression tests:");
        result.Should().Contain("Suggested command: `dotnet test --filter FullyQualifiedName~OrderServiceTests`");
        result.Should().Contain("### Degraded mode");
        result.Should().Contain("`context_pack_status=degraded`");
        result.Should().Contain("failed_step: `impact_analysis`");
        result.Should().Contain("exception: `InvalidOperationException`");
        result.Should().Contain("### Files likely needed");
    }


}

