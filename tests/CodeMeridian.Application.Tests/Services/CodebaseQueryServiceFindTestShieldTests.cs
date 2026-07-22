using CodeMeridian.Application.Services;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace CodeMeridian.Application.Tests.Services;

public sealed class CodebaseQueryServiceFindTestShieldTests : CodebaseQueryServiceAnalyticsTestBase
{
    [Fact]
    public async Task FindTestShieldAsync_WithDirectIndirectAndUnshieldedPath_RendersShieldSections()
    {
        var (sut, graph) = Build();
        var target = Node("m1", "PlaceOrder", CodeNodeType.Method, "src/Orders/OrderService.cs", 12, "Shop");
        var endpoint = Node("e1", "POST /api/orders", CodeNodeType.ApiEndpoint, project: "Shop");
        var frontendCaller = Node("w1", "submitOrder", CodeNodeType.Method, "src/web/orders-client.ts", 22, "Shop.Web");
        var directTest = Node("t1", "OrderServiceTests", CodeNodeType.Class, "tests/Orders/OrderServiceTests.cs", 5, "Shop");
        var routeShield = Node("t2", "OrdersEndpointTests", CodeNodeType.Class, "tests/Api/OrdersEndpointTests.cs", 8, "Shop");
        var repository = Node("repo1", "IOrderRepository", CodeNodeType.Interface, "src/Orders/IOrderRepository.cs", 4, "Shop");

        graph.GetContextForEditingAsync(target.Id, Arg.Any<CancellationToken>())
             .Returns(new EditingContext(target, [endpoint], [], [repository]));
        graph.GetContextForEditingAsync(endpoint.Id, Arg.Any<CancellationToken>())
             .Returns(new EditingContext(endpoint, [], [repository], []));
        graph.GetContextForEditingAsync(frontendCaller.Id, Arg.Any<CancellationToken>())
             .Returns(new EditingContext(frontendCaller, [], [], []));
        graph.FindImpactAsync(target.Id, 2, Arg.Any<CancellationToken>())
             .Returns([(endpoint, 1), (frontendCaller, 2)]);
        graph.FindRelatedTestsAsync(target.Id, "Shop", Arg.Any<CancellationToken>())
             .Returns([(directTest, "direct")]);
        graph.FindRelatedTestsAsync(endpoint.Id, "Shop", Arg.Any<CancellationToken>())
             .Returns([(routeShield, "direct")]);
        graph.FindRelatedTestsAsync(frontendCaller.Id, "Shop", Arg.Any<CancellationToken>())
             .Returns([]);

        var result = await sut.FindTestShieldAsync(target.Id, depth: 2);

        result.Should().Contain("## Test Shield Map");
        result.Should().Contain("1 direct, 1 primary, 0 secondary, 1 unshielded path nodes");
        result.Should().Contain("### Direct test shield (1)");
        result.Should().Contain("OrderServiceTests");
        result.Should().Contain("direct `Calls` edge to `PlaceOrder`");
        result.Should().Contain("### Primary verification tests (1)");
        result.Should().Contain("OrdersEndpointTests");
        result.Should().Contain("directly protects `POST /api/orders`; exact caller-path seam; shares 1 dependency/contract signal with the target slice");
        result.Should().Contain("### Focused verification plan (2)");
        result.Should().Contain("Direct regression tests:");
        result.Should().Contain("OrderServiceTests");
        result.Should().Contain("Contract/API forwarding tests:");
        result.Should().Contain("OrdersEndpointTests");
        result.Should().Contain("### Secondary shield awareness (0)");
        result.Should().Contain("### Unshielded path nodes (1)");
        result.Should().Contain("submitOrder");
        result.Should().Contain("no direct or heuristic related tests found");
        result.Should().Contain("### Suggested test command");
        result.Should().Contain("- none");
    }

    [Fact]
    public async Task FindTestShieldAsync_WhenNodeMissing_ReturnsGuidance()
    {
        var (sut, graph) = Build();
        graph.GetContextForEditingAsync("missing", Arg.Any<CancellationToken>())
             .Returns(new EditingContext(null, [], [], []));

        var result = await sut.FindTestShieldAsync("missing");

        result.Should().Contain("Node `missing` not found");
        result.Should().Contain("resolve_exact_symbol");
    }

    [Fact]
    public async Task FindTestShieldAsync_AllowsUnknownRoleNodesButFiltersGeneratedNodes()
    {
        var (sut, graph) = Build();
        var target = Node("m1", "BuildMinimalContextAsync", CodeNodeType.Method, "src/Application/Services/CodebaseQueryService.Analytics.cs", 480, "CodeMeridian", fileRole: IndexedFileRole.Unknown);
        var directUnknownTest = Node("t1", "CodebaseQueryServiceAnalyticsTests", CodeNodeType.Class, "tests/CodeMeridian.Application.Tests/Services/CodebaseQueryServiceAnalyticsTests.cs", 1, "CodeMeridian", fileRole: IndexedFileRole.Unknown);
        var generatedCaller = Node("g1", "GeneratedClient.Invoke", CodeNodeType.Method, "src/Generated/Client.g.cs", 10, "CodeMeridian", fileRole: IndexedFileRole.Generated);

        graph.GetContextForEditingAsync(target.Id, Arg.Any<CancellationToken>())
             .Returns(new EditingContext(target, [generatedCaller], [], []));
        graph.FindImpactAsync(target.Id, 2, Arg.Any<CancellationToken>())
             .Returns([]);
        graph.FindRelatedTestsAsync(target.Id, "CodeMeridian", Arg.Any<CancellationToken>())
             .Returns([(directUnknownTest, "direct")]);

        var result = await sut.FindTestShieldAsync(target.Id, projectContext: "CodeMeridian", depth: 2);

        result.Should().Contain("CodebaseQueryServiceAnalyticsTests");
        result.Should().NotContain("GeneratedClient.Invoke");
    }

    [Fact]
    public async Task FindTestShieldAsync_KeepsHeuristicTargetTestsInSecondarySection()
    {
        var (sut, graph) = Build();
        var target = Node("m1", "ChargeAsync", CodeNodeType.Method, "src/Payments/PaymentGateway.cs", 42, "Shop");
        var directTest = Node("t1", "PaymentGatewayTests", CodeNodeType.Class, "tests/Payments/PaymentGatewayTests.cs", 5, "Shop");
        var heuristicTest = Node("t2", "ChargeWorkflowTests", CodeNodeType.Class, "tests/Payments/ChargeWorkflowTests.cs", 8, "Shop");

        graph.GetContextForEditingAsync(target.Id, Arg.Any<CancellationToken>())
            .Returns(new EditingContext(target, [], [], []));
        graph.FindImpactAsync(target.Id, 2, Arg.Any<CancellationToken>())
            .Returns([]);
        graph.FindRelatedTestsAsync(target.Id, "Shop", Arg.Any<CancellationToken>())
            .Returns([(directTest, "direct"), (heuristicTest, "heuristic")]);

        var result = await sut.FindTestShieldAsync(target.Id, depth: 2);

        result.Should().Contain("### Primary verification tests (0)");
        result.Should().Contain("### Focused verification plan (2)");
        result.Should().Contain("Direct regression tests:");
        result.Should().Contain("PaymentGatewayTests");
        result.Should().Contain("Heuristic shield tests:");
        result.Should().Contain("ChargeWorkflowTests");
        result.Should().Contain("### Secondary shield awareness (1)");
        result.Should().Contain("ChargeWorkflowTests");
        result.Should().Contain("heuristic match for `ChargeAsync`");
    }

    [Fact]
    public async Task FindTestShieldAsync_DemotesSupportAndContainerNodes_WhenExecutableTestCaseExists()
    {
        var (sut, graph) = Build();
        var target = Node("m1", "PlaceOrder", CodeNodeType.Method, "src/Orders/OrderService.cs", 42, "Shop");
        var testClass = Node("t1", "OrderServiceTests", CodeNodeType.Class, "tests/Orders/OrderServiceTests.cs", 5, "Shop");
        var testCase = Node("t2", "PlaceOrder_ShouldPersist", CodeNodeType.Method, "tests/Orders/OrderServiceTests.cs", 18, "Shop");
        var helper = Node("t3", "OrderTestBuilder.Build", CodeNodeType.Method, "tests/Orders/OrderServiceTests.cs", 10, "Shop");

        graph.GetContextForEditingAsync(target.Id, Arg.Any<CancellationToken>())
            .Returns(new EditingContext(target, [], [], []));
        graph.FindImpactAsync(target.Id, 2, Arg.Any<CancellationToken>())
            .Returns([]);
        graph.FindRelatedTestsAsync(target.Id, "Shop", Arg.Any<CancellationToken>())
            .Returns([(testClass, "direct"), (testCase, "direct"), (helper, "heuristic")]);

        var result = await sut.FindTestShieldAsync(target.Id, depth: 2);

        result.Should().Contain("### Direct test shield (1)");
        result.Should().Contain("PlaceOrder_ShouldPersist");
        result.Should().NotContain("### Direct test shield (2)");
        result.Should().Contain("### Focused verification plan (1)");
        result.Should().Contain("### Secondary shield awareness (2)");
        result.Should().Contain("OrderServiceTests");
        result.Should().Contain("OrderTestBuilder.Build");
        result.Should().Contain("support/container test node");
    }

    [Fact]
    public async Task FindTestShieldAsync_WithSinglePrimaryCandidate_SuggestsFocusedCommand()
    {
        var (sut, graph) = Build(WithDotNetTestCommands());
        var target = Node("m1", "SaveAsync", CodeNodeType.Method, "src/Orders/OrderService.cs", 12, "Shop");
        var endpoint = Node("e1", "POST /api/orders", CodeNodeType.ApiEndpoint, project: "Shop");
        var repository = Node("repo1", "IOrderRepository", CodeNodeType.Interface, "src/Orders/IOrderRepository.cs", 4, "Shop");
        var routeShield = Node("t2", "OrdersEndpointTests", CodeNodeType.Class, "tests/Api/OrdersEndpointTests.cs", 8, "Shop");

        graph.GetContextForEditingAsync(target.Id, Arg.Any<CancellationToken>())
            .Returns(new EditingContext(target, [endpoint], [], [repository]));
        graph.GetContextForEditingAsync(endpoint.Id, Arg.Any<CancellationToken>())
            .Returns(new EditingContext(endpoint, [], [repository], []));
        graph.FindImpactAsync(target.Id, 2, Arg.Any<CancellationToken>())
            .Returns([(endpoint, 1)]);
        graph.FindRelatedTestsAsync(target.Id, "Shop", Arg.Any<CancellationToken>())
            .Returns([]);
        graph.FindRelatedTestsAsync(endpoint.Id, "Shop", Arg.Any<CancellationToken>())
            .Returns([(routeShield, "direct")]);

        var result = await sut.FindTestShieldAsync(target.Id, depth: 2);

        result.Should().Contain("### Suggested test command");
        result.Should().Contain("`dotnet test --filter FullyQualifiedName~OrdersEndpointTests`");
    }

    [Fact]
    public async Task FindTestShieldAsync_WithLegacyFlatTestCommandConfig_StillSuggestsCommand()
    {
        var (sut, graph) = Build(WithLegacyDotNetTestCommands());
        var target = Node("m1", "SaveAsync", CodeNodeType.Method, "src/Orders/OrderService.cs", 12, "Shop");
        var endpoint = Node("e1", "POST /api/orders", CodeNodeType.ApiEndpoint, project: "Shop");
        var repository = Node("repo1", "IOrderRepository", CodeNodeType.Interface, "src/Orders/IOrderRepository.cs", 4, "Shop");
        var routeShield = Node("t2", "OrdersEndpointTests", CodeNodeType.Class, "tests/Api/OrdersEndpointTests.cs", 8, "Shop");

        graph.GetContextForEditingAsync(target.Id, Arg.Any<CancellationToken>())
            .Returns(new EditingContext(target, [endpoint], [], [repository]));
        graph.GetContextForEditingAsync(endpoint.Id, Arg.Any<CancellationToken>())
            .Returns(new EditingContext(endpoint, [], [repository], []));
        graph.FindImpactAsync(target.Id, 2, Arg.Any<CancellationToken>())
            .Returns([(endpoint, 1)]);
        graph.FindRelatedTestsAsync(target.Id, "Shop", Arg.Any<CancellationToken>())
            .Returns([]);
        graph.FindRelatedTestsAsync(endpoint.Id, "Shop", Arg.Any<CancellationToken>())
            .Returns([(routeShield, "direct")]);

        var result = await sut.FindTestShieldAsync(target.Id, depth: 2);

        result.Should().Contain("`dotnet test --filter FullyQualifiedName~OrdersEndpointTests`");
    }

    [Fact]
    public async Task FindTestShieldAsync_WithTypeScriptStrategy_PrefersVitestCommand()
    {
        var (sut, graph) = Build(WithMixedLanguageTestCommands());
        var target = Node("m1", "submitOrder", CodeNodeType.Method, "src/orders.ts", 12, "Shop.Web");
        var directTest = Node("t1", "OrdersSpec", CodeNodeType.Method, "src/orders.spec.ts", 18, "Shop.Web");

        graph.GetContextForEditingAsync(target.Id, Arg.Any<CancellationToken>())
            .Returns(new EditingContext(target, [], [], []));
        graph.FindImpactAsync(target.Id, 2, Arg.Any<CancellationToken>())
            .Returns([]);
        graph.FindRelatedTestsAsync(target.Id, "Shop.Web", Arg.Any<CancellationToken>())
            .Returns([(directTest, "direct")]);

        var result = await sut.FindTestShieldAsync(target.Id, projectContext: "Shop.Web", depth: 2);

        result.Should().Contain("### Suggested test command");
        result.Should().Contain("`vitest run OrdersSpec`");
    }

    [Fact]
    public async Task FindTestShieldAsync_WithoutConfiguredTestCommandStrategy_LeavesSuggestedCommandEmpty()
    {
        var (sut, graph) = Build();
        var target = Node("m1", "SaveAsync", CodeNodeType.Method, "src/Orders/OrderService.cs", 12, "Shop");
        var endpoint = Node("e1", "POST /api/orders", CodeNodeType.ApiEndpoint, project: "Shop");
        var repository = Node("repo1", "IOrderRepository", CodeNodeType.Interface, "src/Orders/IOrderRepository.cs", 4, "Shop");
        var routeShield = Node("t2", "OrdersEndpointTests", CodeNodeType.Class, "tests/Api/OrdersEndpointTests.cs", 8, "Shop");

        graph.GetContextForEditingAsync(target.Id, Arg.Any<CancellationToken>())
            .Returns(new EditingContext(target, [endpoint], [], [repository]));
        graph.GetContextForEditingAsync(endpoint.Id, Arg.Any<CancellationToken>())
            .Returns(new EditingContext(endpoint, [], [repository], []));
        graph.FindImpactAsync(target.Id, 2, Arg.Any<CancellationToken>())
            .Returns([(endpoint, 1)]);
        graph.FindRelatedTestsAsync(target.Id, "Shop", Arg.Any<CancellationToken>())
            .Returns([]);
        graph.FindRelatedTestsAsync(endpoint.Id, "Shop", Arg.Any<CancellationToken>())
            .Returns([(routeShield, "direct")]);

        var result = await sut.FindTestShieldAsync(target.Id, depth: 2);

        result.Should().Contain("### Suggested test command");
        result.Should().Contain("- none");
    }

    [Fact]
    public async Task FindTestShieldAsync_IgnoresNonTestRelatedMatches()
    {
        var (sut, graph) = Build();
        var target = Node("m2", "PlaceOrder", CodeNodeType.Method, "src/Orders/OrderService.cs", 12, "Shop", lineCount: 24);
        var sourceHelper = Node("h1", "AppendRelatedTestsList", CodeNodeType.Method, "src/Application/Helpers/TestFormatting.cs", 44, "Shop", fileRole: IndexedFileRole.Source);
        var realTest = Node("t3", "PlaceOrderTests", CodeNodeType.Class, "tests/Orders/PlaceOrderTests.cs", 18, "Shop", fileRole: IndexedFileRole.Test);

        graph.GetContextForEditingAsync(target.Id, Arg.Any<CancellationToken>())
             .Returns(new EditingContext(target, [], [], []));
        graph.FindImpactAsync(target.Id, 2, Arg.Any<CancellationToken>())
             .Returns([]);
        graph.FindRelatedTestsAsync(target.Id, "Shop", Arg.Any<CancellationToken>())
             .Returns([(sourceHelper, "heuristic"), (realTest, "direct")]);

        var result = await sut.FindTestShieldAsync(target.Id, "Shop");

        result.Should().Contain("PlaceOrderTests");
        result.Should().NotContain("AppendRelatedTestsList");
    }

    [Fact]
    public async Task FindTestShieldAsync_WithNamespaceStyleId_AndCanonicalizableProjectContext_ResolvesBoth()
    {
        var (sut, graph) = Build();
        var target = Node(
            "CodeMeridian::Class::CodeMeridian.Tooling.Configuration.CodeMeridianConfigFileStore",
            "CodeMeridianConfigFileStore",
            CodeNodeType.Class,
            "src/Tooling/Configuration/CodeMeridianConfigFileStore.cs",
            7,
            "CodeMeridian",
            updatedAt: DateTimeOffset.UtcNow,
            lineCount: 503,
            sourceHash: "cfg-store");

        graph.GetProjectContextsAsync("code-meridian", Arg.Any<CancellationToken>())
            .Returns(["CodeMeridian"]);
        graph.QueryNodesAsync(
                Arg.Is<CodeGraphQuery>(q =>
                    q.NameFilter == "CodeMeridianConfigFileStore"
                    && q.ProjectContext == "CodeMeridian"),
                Arg.Any<CancellationToken>())
            .Returns([target]);
        graph.GetContextForEditingAsync(target.Id, Arg.Any<CancellationToken>())
            .Returns(new EditingContext(target, [], [], []));
        graph.FindImpactAsync(target.Id, 2, Arg.Any<CancellationToken>())
            .Returns([]);
        graph.FindRelatedTestsAsync(target.Id, "CodeMeridian", Arg.Any<CancellationToken>())
            .Returns([]);

        var result = await sut.FindTestShieldAsync(
            "CodeMeridian.Tooling.Configuration.CodeMeridianConfigFileStore",
            projectContext: "code-meridian");

        result.Should().Contain("## Test Shield Map");
        result.Should().Contain("CodeMeridianConfigFileStore");
        await graph.Received(1).GetContextForEditingAsync(target.Id, Arg.Any<CancellationToken>());
        await graph.Received(1).FindImpactAsync(target.Id, 2, Arg.Any<CancellationToken>());
        await graph.Received(1).FindRelatedTestsAsync(target.Id, "CodeMeridian", Arg.Any<CancellationToken>());
    }


}

