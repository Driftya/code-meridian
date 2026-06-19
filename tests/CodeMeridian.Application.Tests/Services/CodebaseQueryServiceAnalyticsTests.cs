using CodeMeridian.Application.Services;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace CodeMeridian.Application.Tests.Services;

/// <summary>
/// Tests for the seven graph-analytics methods on <see cref="CodebaseQueryService"/>
/// and its private time-window parser.
/// Each test owns only the mock interactions it asserts — no shared state between tests.
/// </summary>
public sealed class CodebaseQueryServiceAnalyticsTests
{
    // ── Shared factory helpers ────────────────────────────────────────────────

    private static (CodebaseQueryService Sut, ICodeGraphRepository Graph) Build()
    {
        var graph = Substitute.For<ICodeGraphRepository>();
        var vector = Substitute.For<IVectorRepository>();
        return (new CodebaseQueryService(graph, vector), graph);
    }

    private static (CodebaseQueryService Sut, ICodeGraphRepository Graph) Build(CodebaseAnalysisOptions options)
    {
        var graph = Substitute.For<ICodeGraphRepository>();
        var vector = Substitute.For<IVectorRepository>();
        return (new CodebaseQueryService(graph, vector, Options.Create(options)), graph);
    }

    private static (CodebaseQueryService Sut, ICodeGraphRepository Graph, IEmbeddingProvider Embeddings) BuildWithEmbeddings()
    {
        var graph = Substitute.For<ICodeGraphRepository>();
        var vector = Substitute.For<IVectorRepository>();
        var embeddings = Substitute.For<IEmbeddingProvider>();
        return (new CodebaseQueryService(graph, vector, embeddings), graph, embeddings);
    }

    private static CodeNode Node(
        string id,
        string name,
        CodeNodeType type,
        string? file = null,
        int? line = null,
        string? project = null,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? updatedAt = null,
        int? lineCount = null,
        string? summary = null,
        string? sourceSnippet = null,
        string? sourceHash = null,
        IndexedFileRole fileRole = IndexedFileRole.Unknown,
        string? @namespace = null) => new()
    {
        Id = id,
        Name = name,
        Type = type,
        Namespace = @namespace,
        FilePath = file,
        LineNumber = line,
        LineCount = lineCount,
        Summary = summary,
        SourceSnippet = sourceSnippet,
        SourceHash = sourceHash,
        ProjectContext = project,
        CreatedAt = createdAt,
        UpdatedAt = updatedAt,
        FileRole = fileRole
    };

    // ── FindImpactAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task FindImpactAsync_WhenNoCallers_ReturnsGuidanceMessage()
    {
        var (sut, graph) = Build();
        graph.FindImpactAsync("Method:Foo.Bar()", 5, Arg.Any<CancellationToken>())
             .Returns([]);

        var result = await sut.FindImpactAsync("Method:Foo.Bar()");

        result.Should().Contain("No callers found");
        result.Should().Contain("Method:Foo.Bar()");
    }

    [Fact]
    public async Task FindImpactAsync_WithCallers_ReturnsMarkdownTable()
    {
        var (sut, graph) = Build();
        graph.FindImpactAsync("Method:Foo.Bar()", 3, Arg.Any<CancellationToken>())
             .Returns([(Node("c1", "Caller", CodeNodeType.Method, "src/Caller.cs"), 1),
                       (Node("c2", "Indirect", CodeNodeType.Class), 2)]);

        var result = await sut.FindImpactAsync("Method:Foo.Bar()", depth: 3);

        result.Should().Contain("## Impact Analysis");
        result.Should().Contain("**2** code elements");
        result.Should().Contain("Caller");
        result.Should().Contain("src/Caller.cs");
        result.Should().Contain("| 1 |");
        result.Should().Contain("| 2 |");
        result.Should().Contain("—"); // missing FilePath rendered as dash
    }

    // ── FindHotspotsAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task FindImpactAsync_WithConfidence_SeparatesProvenHeuristicAndUnknownRisk()
    {
        var (sut, graph) = Build();
        var target = Node("target", "ChargeAsync", CodeNodeType.Method, "src/Payments/PaymentGateway.cs", 42, "Shop", updatedAt: DateTimeOffset.UtcNow);
        var provenCaller = Node("caller-1", "CheckoutService.PlaceOrder", CodeNodeType.Method, "src/Checkout/CheckoutService.cs", 18, "Shop", updatedAt: DateTimeOffset.UtcNow);
        var routeNode = Node("route-1", "POST /api/payments", CodeNodeType.ApiEndpoint, "src/Api/PaymentsEndpoint.cs", 9, "Shop.Api", updatedAt: DateTimeOffset.UtcNow);
        var staleCaller = Node("caller-2", "LegacyBatchJob.Run", CodeNodeType.Method, project: "Shop.Legacy");

        graph.FindImpactPathsAsync("Method:Payments.PaymentGateway.ChargeAsync", 3, Arg.Any<CancellationToken>())
             .Returns([
                 new ImpactPath(
                     provenCaller,
                     1,
                     [
                         new GraphPathStep(provenCaller, "Calls", null),
                         new GraphPathStep(target, null, null)
                     ]),
                 new ImpactPath(
                     routeNode,
                     2,
                     [
                         new GraphPathStep(routeNode, "Calls", null),
                         new GraphPathStep(target, null, null)
                     ]),
                 new ImpactPath(
                     staleCaller,
                     1,
                     [
                         new GraphPathStep(staleCaller, "Calls", null),
                         new GraphPathStep(target, null, null)
                     ])
             ]);

        var result = await sut.FindImpactAsync(
            "Method:Payments.PaymentGateway.ChargeAsync",
            depth: 3,
            includeConfidence: true);

        result.Should().Contain("## Impact Analysis");
        result.Should().Contain("**Impact confidence:** Low");
        result.Should().Contain("### Proven callers (1)");
        result.Should().Contain("CheckoutService.PlaceOrder");
        result.Should().Contain("direct structural path");
        result.Should().Contain("### Heuristic callers (1)");
        result.Should().Contain("POST /api/payments");
        result.Should().Contain("path crosses route or knowledge nodes");
        result.Should().Contain("### Unknown risk (1)");
        result.Should().Contain("LegacyBatchJob.Run");
        result.Should().Contain("node has no file path");
    }

    [Fact]
    public async Task FindHotspotsAsync_WhenEmpty_ReturnsNoHotspotsMessage()
    {
        var (sut, graph) = Build();
        graph.FindHotspotsAsync("Proj", Arg.Any<int>(), Arg.Any<CancellationToken>())
             .Returns([]);

        var result = await sut.FindHotspotsAsync("Proj");

        result.Should().Contain("No hotspots found");
        result.Should().Contain("'Proj'");
    }

    [Fact]
    public async Task FindHotspotsAsync_WithResults_ContainsRankAndFanIn()
    {
        var (sut, graph) = Build();
        graph.FindHotspotsAsync(null, Arg.Any<int>(), Arg.Any<CancellationToken>())
             .Returns([(Node("h1", "CoreService", CodeNodeType.Class, "src/Core.cs"), 42),
                       (Node("h2", "UtilHelper", CodeNodeType.Class), 17)]);

        var result = await sut.FindHotspotsAsync();

        result.Should().Contain("## Coupling Hotspots");
        result.Should().Contain("CoreService");
        result.Should().Contain("42");
        result.Should().Contain("UtilHelper");
        result.Should().Contain("17");
        result.Should().Contain("| 1 |"); // rank 1
        result.Should().Contain("| 2 |"); // rank 2
    }

    // ── FindConnectionAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task FindConnectionAsync_WhenNoPath_ReturnsNoPathMessage()
    {
        var (sut, graph) = Build();
        graph.FindConnectionAsync("A", "B", Arg.Any<CancellationToken>())
             .Returns([]);

        var result = await sut.FindConnectionAsync("A", "B");

        result.Should().Contain("No path found");
        result.Should().Contain("`A`");
        result.Should().Contain("`B`");
    }

    [Fact]
    public async Task FindConnectionAsync_WithPath_ListsHops()
    {
        var (sut, graph) = Build();
        graph.FindConnectionAsync("A", "C", Arg.Any<CancellationToken>())
             .Returns([(Node("a", "Alpha", CodeNodeType.Class, "src/Alpha.cs"), (string?)null),
                       (Node("b", "Beta",  CodeNodeType.Method, "src/Beta.cs"),  "Calls"),
                       (Node("c", "Gamma", CodeNodeType.Class,  "src/Gamma.cs"), "Uses")]);

        var result = await sut.FindConnectionAsync("A", "C");

        result.Should().Contain("## Connection");
        result.Should().Contain("2 hops");
        result.Should().Contain("Alpha");
        result.Should().Contain("Beta");
        result.Should().Contain("Gamma");
        result.Should().Contain("—[Calls]→");
        result.Should().Contain("—[Uses]→");
    }

    // ── FindUnreferencedAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task FindUnreferencedAsync_WhenEmpty_ReturnsAllReferencedMessage()
    {
        var (sut, graph) = Build();
        graph.FindUnreferencedAsync(null, Arg.Any<CancellationToken>())
             .Returns([]);

        var result = await sut.FindUnreferencedAsync();

        result.Should().Contain("No unreferenced methods or classes found");
        result.Should().Contain("Everything appears to be referenced");
    }

    [Fact]
    public async Task FindConnectionAsync_WithRouteLinkedPath_IncludesApiEndpointHop()
    {
        var (sut, graph) = Build();
        graph.FindConnectionAsync("frontend", "backend", Arg.Any<CancellationToken>())
             .Returns([
                 (Node("frontend", "loadOrders", CodeNodeType.Method, "src/web/orders.ts", project: "Shop.Web"), (string?)null),
                 (Node("route", "POST /api/orders", CodeNodeType.ApiEndpoint, project: "Shop.Api"), "Calls"),
                 (Node("backend", "CreateOrder", CodeNodeType.Method, "src/api/OrdersController.cs", project: "Shop.Api"), "Uses")
             ]);

        var result = await sut.FindConnectionAsync("frontend", "backend");

        result.Should().Contain("POST /api/orders");
        result.Should().Contain("**ApiEndpoint**");
        result.Should().Contain("loadOrders");
        result.Should().Contain("CreateOrder");
    }

    [Fact]
    public async Task FindUnreferencedAsync_WithResults_GroupsByTypeAndIncludesDisclaimer()
    {
        var (sut, graph) = Build();
        graph.FindUnreferencedAsync("Proj", Arg.Any<CancellationToken>())
             .Returns([Node("m1", "OldMethod", CodeNodeType.Method, "src/Old.cs", 42, "Proj"),
                       Node("c1", "DeadClass", CodeNodeType.Class, "src/Dead.cs", null, "Proj")]);

        var result = await sut.FindUnreferencedAsync("Proj");

        result.Should().Contain("## Unreferenced Code — Proj");
        result.Should().Contain("**2** methods/classes");
        result.Should().Contain("### Classs"); // grouped header
        result.Should().Contain("### Methods");
        result.Should().Contain("`OldMethod`");
        result.Should().Contain(":42");
        result.Should().Contain("entry points");
    }

    // ── FindCrossProjectDependenciesAsync ─────────────────────────────────────

    [Fact]
    public async Task FindCrossProjectDependenciesAsync_WhenEmpty_ReturnsWithinSingleProjectsMessage()
    {
        var (sut, graph) = Build();
        graph.FindCrossProjectDependenciesAsync(null, Arg.Any<CancellationToken>())
             .Returns([]);

        var result = await sut.FindCrossProjectDependenciesAsync();

        result.Should().Contain("No cross-project dependencies found");
        result.Should().Contain("within single projects");
    }

    [Fact]
    public async Task FindCrossProjectDependenciesAsync_WithEdges_ReturnsTable()
    {
        var (sut, graph) = Build();
        var src = Node("s1", "OrderSvc", CodeNodeType.Class, project: "Api");
        var tgt = Node("t1", "IRepo",    CodeNodeType.Interface, project: "Core");

        graph.FindCrossProjectDependenciesAsync(null, Arg.Any<CancellationToken>())
             .Returns([(src, tgt, "DependsOn")]);

        var result = await sut.FindCrossProjectDependenciesAsync();

        result.Should().Contain("## Cross-Project Dependencies");
        result.Should().Contain("**1** edges");
        result.Should().Contain("Api");
        result.Should().Contain("OrderSvc");
        result.Should().Contain("DependsOn");
        result.Should().Contain("IRepo");
        result.Should().Contain("Core");
    }

    [Fact]
    public async Task FindCrossProjectDependenciesAsync_WithProjectFilter_IncludesFilterInHeader()
    {
        var (sut, graph) = Build();
        graph.FindCrossProjectDependenciesAsync("Api", Arg.Any<CancellationToken>())
             .Returns([]);

        var result = await sut.FindCrossProjectDependenciesAsync("Api");

        result.Should().Contain("involving 'Api'");
    }

    // ── FindCoverageGapsAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task FindCoverageGapsAsync_WhenEmpty_ReturnsAllCoveredMessage()
    {
        var (sut, graph) = Build();
        graph.FindCoverageGapsAsync(null, Arg.Any<CancellationToken>())
             .Returns([]);

        var result = await sut.FindCoverageGapsAsync();

        result.Should().Contain("No coverage gaps found");
        result.Should().Contain("All production classes and methods appear");
    }

    [Fact]
    public async Task FindCoverageGapsAsync_WithGaps_GroupsByTypeAndIncludesDisclaimer()
    {
        var (sut, graph) = Build();
        graph.FindCoverageGapsAsync("MyApi", Arg.Any<CancellationToken>())
             .Returns([Node("u1", "UntouchedService", CodeNodeType.Class, "src/Untouched.cs", project: "MyApi"),
                       Node("u2", "OrphanMethod",    CodeNodeType.Method, "src/Orphan.cs", 10, "MyApi")]);

        var result = await sut.FindCoverageGapsAsync("MyApi");

        result.Should().Contain("## Test Coverage Gaps — MyApi");
        result.Should().Contain("**2** production");
        result.Should().Contain("ranked by likely risk");
        result.Should().Contain("`UntouchedService`");
        result.Should().Contain("`OrphanMethod`");
        result.Should().Contain(":10");
        result.Should().Contain("heuristic");
    }

    [Fact]
    public async Task FindCoverageGapsAsync_IncludesUnknownRoleNodesButFiltersGeneratedNodes()
    {
        var (sut, graph) = Build();
        graph.FindCoverageGapsAsync("CodeMeridian", Arg.Any<CancellationToken>())
             .Returns([
                 Node("u1", "LegacyService", CodeNodeType.Class, "src/LegacyService.cs", project: "CodeMeridian", fileRole: IndexedFileRole.Unknown),
                 Node("u2", "GeneratedMapper", CodeNodeType.Class, "src/Generated/Mapper.g.cs", project: "CodeMeridian", fileRole: IndexedFileRole.Generated)
             ]);

        var result = await sut.FindCoverageGapsAsync("CodeMeridian");

        result.Should().Contain("LegacyService");
        result.Should().NotContain("GeneratedMapper");
    }

    // ── FindRecentlyChangedAsync ──────────────────────────────────────────────

    [Fact]
    public async Task FindTestShieldAsync_WithDirectIndirectAndUnshieldedPath_RendersShieldSections()
    {
        var (sut, graph) = Build();
        var target = Node("m1", "PlaceOrder", CodeNodeType.Method, "src/Orders/OrderService.cs", 12, "Shop");
        var endpoint = Node("e1", "POST /api/orders", CodeNodeType.ApiEndpoint, project: "Shop");
        var frontendCaller = Node("w1", "submitOrder", CodeNodeType.Method, "src/web/orders-client.ts", 22, "Shop.Web");
        var directTest = Node("t1", "OrderServiceTests", CodeNodeType.Class, "tests/Orders/OrderServiceTests.cs", 5, "Shop");
        var routeShield = Node("t2", "OrdersEndpointTests", CodeNodeType.Class, "tests/Api/OrdersEndpointTests.cs", 8, "Shop");

        graph.GetContextForEditingAsync(target.Id, Arg.Any<CancellationToken>())
             .Returns(new EditingContext(target, [endpoint], [], []));
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
        result.Should().Contain("1 direct, 1 indirect, 1 unshielded path nodes");
        result.Should().Contain("### Direct test shield (1)");
        result.Should().Contain("OrderServiceTests");
        result.Should().Contain("direct `Calls` edge to `PlaceOrder`");
        result.Should().Contain("### Indirect test shield (1)");
        result.Should().Contain("OrdersEndpointTests");
        result.Should().Contain("directly protects `POST /api/orders` on the caller path");
        result.Should().Contain("### Unshielded path nodes (1)");
        result.Should().Contain("submitOrder");
        result.Should().Contain("no direct or heuristic related tests found");
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
    public async Task FindRecentlyChangedAsync_WhenEmpty_ReturnsNoChangesMessage()
    {
        var (sut, graph) = Build();
        graph.FindRecentlyChangedAsync(null, Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
             .Returns([]);

        var result = await sut.FindRecentlyChangedAsync(window: "7d");

        result.Should().Contain("No nodes created or updated in the last 7d");
        result.Should().Contain("timestamps are only tracked");
    }

    [Fact]
    public async Task FindRecentlyChangedAsync_WithResults_ReturnsAgeTable()
    {
        var (sut, graph) = Build();
        var changedAt = DateTimeOffset.UtcNow.AddHours(-2);

        graph.FindRecentlyChangedAsync("MyApi", Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
             .Returns([(Node("n1", "NewHandler", CodeNodeType.Class, "src/Handler.cs"), changedAt, "created")]);

        var result = await sut.FindRecentlyChangedAsync("MyApi", "24h");

        result.Should().Contain("## Recently Changed — last 24h in MyApi");
        result.Should().Contain("**1** nodes changed");
        result.Should().Contain("NewHandler");
        result.Should().Contain("created");
        result.Should().Contain("h ago"); // "2h ago"
    }

    [Fact]
    public async Task FindRecentlyChangedAsync_VeryRecentNode_ShowsMinutesAgo()
    {
        var (sut, graph) = Build();
        var changedAt = DateTimeOffset.UtcNow.AddMinutes(-15);

        graph.FindRecentlyChangedAsync(null, Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
             .Returns([(Node("n1", "Brand New", CodeNodeType.Method), changedAt, "updated")]);

        var result = await sut.FindRecentlyChangedAsync();

        result.Should().Contain("m ago"); // "15m ago"
    }

    [Fact]
    public async Task FindRecentlyChangedAsync_OlderThanADay_ShowsDaysAgo()
    {
        var (sut, graph) = Build();
        var changedAt = DateTimeOffset.UtcNow.AddDays(-3);

        graph.FindRecentlyChangedAsync(null, Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
             .Returns([(Node("n1", "Old Change", CodeNodeType.Class), changedAt, "created")]);

        var result = await sut.FindRecentlyChangedAsync();

        result.Should().Contain("d ago"); // "3d ago"
    }

    // ── ParseWindow (via observable behaviour) ────────────────────────────────

    [Theory]
    [InlineData("24h", 24 * 60)]
    [InlineData("1h",   1 * 60)]
    [InlineData("7d",  7 * 24 * 60)]
    [InlineData("30m", 30)]
    [InlineData("garbage", 24 * 60)] // default fallback
    [InlineData("",     24 * 60)]    // empty → default
    public async Task ParseWindow_ConvertsToCorrectTimeSpan(string window, int expectedMinutes)
    {
        var (sut, graph) = Build();

        TimeSpan? captured = null;

        graph.FindRecentlyChangedAsync(
                Arg.Any<string?>(),
                Arg.Do<TimeSpan>(ts => captured = ts),
                Arg.Any<CancellationToken>())
             .Returns([]);

        await sut.FindRecentlyChangedAsync(window: window);

        captured.Should().NotBeNull();
        captured!.Value.TotalMinutes.Should().BeApproximately(expectedMinutes, precision: 0.1);
    }

    // ── FindLargeNodesAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task FindLargeNodesAsync_WhenNoResults_ReturnsGuidanceMessage()
    {
        var (sut, graph) = Build();
        graph.FindLargeNodesAsync("Proj", Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
             .Returns([]);

        var result = await sut.FindLargeNodesAsync("Proj");

        result.Should().Contain("No large classes");
        result.Should().Contain("Re-index");
    }

    [Fact]
    public async Task FindLargeNodesAsync_WithLargeClass_ReturnsTableWithLineCount()
    {
        var (sut, graph) = Build();
        var bigClass = NodeWithLineCount("c1", "BigService", CodeNodeType.Class, "src/Big.cs", 10, 520);
        graph.FindLargeNodesAsync(null, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
             .Returns([bigClass]);

        var result = await sut.FindLargeNodesAsync();

        result.Should().Contain("## Large Node Analysis");
        result.Should().Contain("**1** oversized");
        result.Should().Contain("520");
        result.Should().Contain("`BigService`");
        result.Should().Contain("src/Big.cs");
        result.Should().Contain(":10");
        result.Should().Contain("SRP");
    }

    [Fact]
    public async Task FindLargeNodesAsync_WithMixedTypes_GroupsClassesAndMethodsSeparately()
    {
        var (sut, graph) = Build();
        graph.FindLargeNodesAsync(null, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
             .Returns([
                 NodeWithLineCount("c1", "HugeClass", CodeNodeType.Class, lineCount: 600),
                 NodeWithLineCount("m1", "LongMethod", CodeNodeType.Method, lineCount: 80),
             ]);

        var result = await sut.FindLargeNodesAsync();

        result.Should().Contain("### Classs");
        result.Should().Contain("### Methods");
        result.Should().Contain("HugeClass");
        result.Should().Contain("LongMethod");
    }

    // ── GetContextForEditingAsync ─────────────────────────────────────────────

    [Fact]
    public async Task GetContextForEditingAsync_WhenNodeNotFound_ReturnsNotFoundMessage()
    {
        var (sut, graph) = Build();
        graph.GetContextForEditingAsync("Unknown::Method", Arg.Any<CancellationToken>())
             .Returns(new EditingContext(null, [], [], []));

        var result = await sut.GetContextForEditingAsync("Unknown::Method");

        result.Should().Contain("not found in the graph");
        result.Should().Contain("Unknown::Method");
        result.Should().Contain("query_codebase");
    }

    [Fact]
    public async Task GetContextForEditingAsync_WithCallers_ShowsCallerSection()
    {
        var (sut, graph) = Build();
        var target = NodeWithLineCount("t1", "SaveAsync", CodeNodeType.Method, "src/Repo.cs", 42, 25);
        var caller = Node("ca1", "OrderController.Create", CodeNodeType.Method, "src/Ctrl.cs", 10);

        graph.GetContextForEditingAsync("t1", Arg.Any<CancellationToken>())
             .Returns(new EditingContext(target, [caller], [], []));

        var result = await sut.GetContextForEditingAsync("t1");

        result.Should().Contain("## Edit Context — `SaveAsync`");
        result.Should().Contain("25 lines");
        result.Should().Contain("### Callers (1)");
        result.Should().Contain("`OrderController.Create`");
        result.Should().Contain("src/Ctrl.cs");
        result.Should().Contain(":10");
    }

    [Fact]
    public async Task GetContextForEditingAsync_WithNoCallers_ShowsSafeToChangeSignature()
    {
        var (sut, graph) = Build();
        var target = Node("t1", "InternalHelper", CodeNodeType.Method, "src/Helper.cs");

        graph.GetContextForEditingAsync("t1", Arg.Any<CancellationToken>())
             .Returns(new EditingContext(target, [], [], []));

        var result = await sut.GetContextForEditingAsync("t1");

        result.Should().Contain("safe to change signature");
    }

    [Fact]
    public async Task GetContextForEditingAsync_WithCalleesAndInterfaces_IncludesBothSections()
    {
        var (sut, graph) = Build();
        var target = Node("t1", "ProcessOrder", CodeNodeType.Method, "src/Order.cs");
        var callee = Node("ce1", "SaveAsync", CodeNodeType.Method, "src/Repo.cs");
        var iface = Node("i1", "IOrderRepository", CodeNodeType.Interface);

        graph.GetContextForEditingAsync("t1", Arg.Any<CancellationToken>())
             .Returns(new EditingContext(target, [], [callee], [iface]));

        var result = await sut.GetContextForEditingAsync("t1");

        result.Should().Contain("### Calls (1)");
        result.Should().Contain("`SaveAsync`");
        result.Should().Contain("### Implements");
        result.Should().Contain("`IOrderRepository`");
        result.Should().Contain("find_impact");
    }

    // ── FindGodClassesAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task FindGodClassesAsync_WhenEmpty_ReturnsGuidanceMessage()
    {
        var (sut, graph) = Build();
        graph.FindGodClassesAsync("Proj", Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
             .Returns([]);

        var result = await sut.FindGodClassesAsync("Proj");

        result.Should().Contain("No god classes found");
        result.Should().Contain("re-index");
    }

    [Fact]
    public async Task FindGodClassesAsync_WithCriticalClass_ReturnsRiskTable()
    {
        var (sut, graph) = Build();
        var godClass = NodeWithLineCount("g1", "MegaService", CodeNodeType.Class, "src/Mega.cs", lineCount: 800);

        graph.FindGodClassesAsync(null, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
             .Returns([(godClass, 800, 40)]);

        var result = await sut.FindGodClassesAsync();

        result.Should().Contain("## God Classes");
        result.Should().Contain("Critical");
        result.Should().Contain("800");
        result.Should().Contain("40");
        result.Should().Contain("`MegaService`");
        result.Should().Contain("get_context_for_editing");
    }

    [Fact]
    public async Task FindGodClassesAsync_WithMultipleRisks_RendersAllRows()
    {
        var (sut, graph) = Build();
        var c1 = NodeWithLineCount("g1", "CriticalSvc", CodeNodeType.Class, lineCount: 500);
        var c2 = NodeWithLineCount("g2", "MediumSvc",   CodeNodeType.Class, lineCount: 350);

        graph.FindGodClassesAsync(null, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
             .Returns([(c1, 500, 40), (c2, 350, 4)]);

        var result = await sut.FindGodClassesAsync();

        result.Should().Contain("CriticalSvc");
        result.Should().Contain("MediumSvc");
        result.Should().Contain("Critical");
        result.Should().Contain("Medium");
    }

    [Fact]
    public async Task FindGodClassesAsync_FiltersTestsAndMigrationsByDefault()
    {
        var (sut, graph) = Build();

        graph.FindGodClassesAsync(null, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
             .Returns([
                 (Node("source", "OrderService", CodeNodeType.Class, "src/OrderService.cs", lineCount: 420, fileRole: IndexedFileRole.Source), 420, 6),
                 (Node("test", "OrderServiceTests", CodeNodeType.Class, "tests/OrderServiceTests.cs", lineCount: 600, fileRole: IndexedFileRole.Test), 600, 10),
                 (Node("migration", "CreateUsers", CodeNodeType.Class, "src/Migrations/CreateUsers.cs", lineCount: 700, fileRole: IndexedFileRole.Migration), 700, 12)
             ]);

        var result = await sut.FindGodClassesAsync();

        result.Should().Contain("OrderService");
        result.Should().NotContain("OrderServiceTests");
        result.Should().NotContain("CreateUsers");
    }

    // ── Factory helpers for line-count nodes ──────────────────────────────────

    private static CodeNode NodeWithLineCount(
        string id,
        string name,
        CodeNodeType type,
        string? file = null,
        int? line = null,
        int? lineCount = null) => new()
    {
        Id = id,
        Name = name,
        Type = type,
        FilePath = file,
        LineNumber = line,
        LineCount = lineCount
    };

    // ── FindDownstreamAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task FindDownstreamAsync_WhenNoDownstream_ReturnsGuidanceMessage()
    {
        var (sut, graph) = Build();
        graph.FindDownstreamAsync("Method:Foo.Bar()", 5, Arg.Any<CancellationToken>())
             .Returns([]);

        var result = await sut.FindDownstreamAsync("Method:Foo.Bar()");

        result.Should().Contain("No downstream dependencies found");
        result.Should().Contain("Method:Foo.Bar()");
    }

    [Fact]
    public async Task FindDownstreamAsync_WithResults_ReturnsMarkdownTable()
    {
        var (sut, graph) = Build();
        graph.FindDownstreamAsync("Method:Foo.Bar()", 3, Arg.Any<CancellationToken>())
             .Returns([(Node("d1", "DepService", CodeNodeType.Class, "src/Dep.cs"), 1),
                       (Node("d2", "DbRepo", CodeNodeType.Class), 2)]);

        var result = await sut.FindDownstreamAsync("Method:Foo.Bar()", depth: 3);

        result.Should().Contain("## Downstream Blast Radius");
        result.Should().Contain("**2** elements");
        result.Should().Contain("DepService");
        result.Should().Contain("src/Dep.cs");
        result.Should().Contain("| 1 |");
        result.Should().Contain("| 2 |");
        result.Should().Contain("find_impact");
    }

    // ── FindCyclesAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task FindCyclesAsync_WhenNoCycles_ReturnsCleanMessage()
    {
        var (sut, graph) = Build();
        graph.FindCyclesAsync(null, Arg.Any<CancellationToken>())
             .Returns([]);

        var result = await sut.FindCyclesAsync();

        result.Should().Contain("No namespace-level circular dependencies found");
        result.Should().Contain("Clean architecture");
    }

    [Fact]
    public async Task FindCyclesAsync_WithCycles_ReturnsTable()
    {
        var (sut, graph) = Build();
        graph.FindCyclesAsync("Proj", Arg.Any<CancellationToken>())
             .Returns([("MyApp.Services", "MyApp.Repositories")]);

        var result = await sut.FindCyclesAsync("Proj");

        result.Should().Contain("## Circular Dependencies");
        result.Should().Contain("**1** namespace pairs");
        result.Should().Contain("`MyApp.Services`");
        result.Should().Contain("`MyApp.Repositories`");
        result.Should().Contain("↔");
        result.Should().Contain("abstraction");
    }

    // ── FindArchitectureViolationsAsync ───────────────────────────────────────

    [Fact]
    public async Task FindArchitectureErosionTimelineAsync_WithSignals_ReturnsDailyTrend()
    {
        var (sut, graph) = Build();
        var oldDate = DateTimeOffset.UtcNow.AddDays(-20);
        var currentDate = DateTimeOffset.UtcNow;
        var source = Node("s1", "CoreService", CodeNodeType.Class, "src/Core/Svc.cs", updatedAt: oldDate);
        var target = Node("t1", "DbContext", CodeNodeType.Class, "src/Infrastructure/Db.cs", updatedAt: oldDate);
        var godClass = Node(
            "g1",
            "MegaService",
            CodeNodeType.Class,
            "src/MegaService.cs",
            updatedAt: currentDate,
            lineCount: 700,
            fileRole: IndexedFileRole.Source);

        graph.FindArchitectureViolationsAsync("Shop", Arg.Any<CancellationToken>())
             .Returns([(source, target, "Core -> Infrastructure")]);
        graph.FindCyclesAsync("Shop", Arg.Any<CancellationToken>())
             .Returns([("Shop.Core", "Shop.Infrastructure")]);
        graph.FindGodClassesAsync("Shop", 300, 3, Arg.Any<CancellationToken>())
             .Returns([(godClass, 700, 8)]);

        var result = await sut.FindArchitectureErosionTimelineAsync("Shop", days: 2);

        result.Should().Contain("## Architecture Erosion Timeline - Shop");
        result.Should().Contain("| Date | Cross-layer refs | Cycles | God classes | God-class lines |");
        result.Should().Contain("Current snapshot");
        result.Should().Contain("Cross-layer references: 1");
        result.Should().Contain("Circular namespace dependencies: 1");
        result.Should().Contain("God classes: 1");
        result.Should().Contain("God-class total lines: 700");
        result.Should().Contain("Resolved or deleted historical violations are not recoverable");
    }

    [Fact]
    public async Task FindArchitectureErosionTimelineAsync_WhenClean_ReturnsCleanMessage()
    {
        var (sut, graph) = Build();
        graph.FindArchitectureViolationsAsync(null, Arg.Any<CancellationToken>())
             .Returns([]);
        graph.FindCyclesAsync(null, Arg.Any<CancellationToken>())
             .Returns([]);
        graph.FindGodClassesAsync(null, 300, 3, Arg.Any<CancellationToken>())
             .Returns([]);

        var result = await sut.FindArchitectureErosionTimelineAsync(days: 30);

        result.Should().Contain("No architecture erosion signals found");
        result.Should().Contain("god-class thresholds are currently clean");
    }

    [Fact]
    public async Task FindArchitectureViolationsAsync_WhenNoViolations_ReturnsCleanMessage()
    {
        var (sut, graph) = Build();
        graph.FindArchitectureViolationsAsync(null, Arg.Any<CancellationToken>())
             .Returns([]);

        var result = await sut.FindArchitectureViolationsAsync();

        result.Should().Contain("No architecture violations found");
        result.Should().Contain("Configured architecture layers");
    }

    [Fact]
    public async Task FindArchitectureViolationsAsync_WithViolations_ReturnsTable()
    {
        var (sut, graph) = Build();
        var source = Node("s1", "CoreService", CodeNodeType.Class, "src/Core/Svc.cs");
        var target = Node("t1", "DbContext",   CodeNodeType.Class, "src/Infrastructure/Db.cs");

        graph.FindArchitectureViolationsAsync(null, Arg.Any<CancellationToken>())
             .Returns([(source, target, "Core → MyApp.Infrastructure")]);

        var result = await sut.FindArchitectureViolationsAsync();

        result.Should().Contain("## Architecture Violations");
        result.Should().Contain("configured architecture layer rules");
        result.Should().Contain("CoreService");
        result.Should().Contain("DbContext");
        result.Should().Contain("Core → MyApp.Infrastructure");
        result.Should().Contain(".meridian/architecture.json");
    }

    // ── FindHighChurnAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task FindSmellPathsAsync_WhenNoPaths_ReturnsCleanMessage()
    {
        var (sut, graph) = Build();
        graph.FindSmellPathsAsync(null, 4, Arg.Any<CancellationToken>())
            .Returns([]);

        var result = await sut.FindSmellPathsAsync();

        result.Should().Contain("No dependency smell paths found");
        result.Should().Contain("forbidden layer-to-layer paths");
    }

    [Fact]
    public async Task FindSmellPathsAsync_WithPaths_ReturnsPathTable()
    {
        var (sut, graph) = Build();
        var source = Node("source", "PricingRules", CodeNodeType.Class, "src/Core/PricingRules.cs");
        var middle = Node("middle", "SqlOrderRepository", CodeNodeType.Class, "src/Application/Orders/SqlOrderRepository.cs");
        var target = Node("target", "Neo4jOrderStore", CodeNodeType.Class, "src/Infrastructure/Orders/Neo4jOrderStore.cs");

        graph.FindSmellPathsAsync("Shop", 4, Arg.Any<CancellationToken>())
            .Returns([
                new DependencySmellPath(
                    "Core → Infrastructure",
                    source,
                    target,
                    2,
                    [
                        new GraphPathStep(source, "Uses", null),
                        new GraphPathStep(middle, "DependsOn", null),
                        new GraphPathStep(target, null, null)
                    ])
            ]);

        var result = await sut.FindSmellPathsAsync("Shop");

        result.Should().Contain("## Dependency Smell Paths");
        result.Should().Contain("**1** shortest forbidden dependency paths");
        result.Should().Contain("Core → Infrastructure");
        result.Should().Contain("PricingRules");
        result.Should().Contain("Neo4jOrderStore");
        result.Should().Contain("`PricingRules` -[Uses]- `SqlOrderRepository` -[DependsOn]- `Neo4jOrderStore`");
        result.Should().Contain("safe-first version");
    }

    [Fact]
    public async Task FindHighChurnAsync_WhenNoHighChurn_ReturnsStableMessage()
    {
        var (sut, graph) = Build();
        graph.FindHighChurnAsync("Proj", Arg.Any<int>(), Arg.Any<CancellationToken>())
             .Returns([]);

        var result = await sut.FindHighChurnAsync("Proj");

        result.Should().Contain("No high-churn nodes found");
        result.Should().Contain("'Proj'");
    }

    [Fact]
    public async Task FindHighChurnAsync_WithResults_ReturnsTable()
    {
        var (sut, graph) = Build();
        graph.FindHighChurnAsync(null, 3, Arg.Any<CancellationToken>())
             .Returns([(Node("n1", "HotFile", CodeNodeType.Class, "src/Hot.cs"), 12),
                       (Node("n2", "BusyHelper", CodeNodeType.Method), 5)]);

        var result = await sut.FindHighChurnAsync();

        result.Should().Contain("## High-Churn Nodes");
        result.Should().Contain("**2** nodes");
        result.Should().Contain("Production code is ranked ahead");
        result.Should().Contain("HotFile");
        result.Should().Contain("12×");
        result.Should().Contain("5×");
        result.Should().Contain("find_hotspots");
    }

    [Fact]
    public async Task FindHighChurnAsync_WithRankingOptions_RanksProductionBeforeTests()
    {
        var (sut, graph) = Build(new CodebaseAnalysisOptions
        {
            Ranking = new RankingOptions
            {
                PreferProductionOverTests = true,
                TestPathContains = ["tests/"]
            }
        });
        graph.FindHighChurnAsync(null, 3, Arg.Any<CancellationToken>())
            .Returns([
                (Node("t1", "Build", CodeNodeType.Method, "tests/ServiceTests.cs"), 20),
                (Node("p1", "RealService", CodeNodeType.Class, "src/RealService.cs"), 3)
            ]);

        var result = await sut.FindHighChurnAsync();

        result.IndexOf("RealService", StringComparison.Ordinal)
            .Should()
            .BeLessThan(result.IndexOf("Build", StringComparison.Ordinal));
    }

    // ── GetPageRankAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetPageRankAsync_WhenEmpty_ReturnsNoEdgesMessage()
    {
        var (sut, graph) = Build();
        graph.GetPageRankAsync(null, Arg.Any<int>(), Arg.Any<CancellationToken>())
             .Returns([]);

        var result = await sut.GetPageRankAsync();

        result.Should().Contain("No results from PageRank");
        result.Should().Contain("Calls/Uses/DependsOn edges");
    }

    [Fact]
    public async Task GetPageRankAsync_WhenGdsFails_ReturnsInstallGuidance()
    {
        var (sut, graph) = Build();
        graph.GetPageRankAsync(null, Arg.Any<int>(), Arg.Any<CancellationToken>())
             .ThrowsAsync(new InvalidOperationException("No such procedure: gds.pageRank.stream"));

        var result = await sut.GetPageRankAsync();

        result.Should().Contain("PageRank failed");
        result.Should().Contain("Graph Data Science");
    }

    [Fact]
    public async Task GetPageRankAsync_WithResults_ReturnsRankedTable()
    {
        var (sut, graph) = Build();
        graph.GetPageRankAsync(null, Arg.Any<int>(), Arg.Any<CancellationToken>())
             .Returns([(Node("n1", "CoreHub", CodeNodeType.Class, "src/Core.cs"), 0.87),
                       (Node("n2", "BaseRepo", CodeNodeType.Class), 0.43)]);

        var result = await sut.GetPageRankAsync();

        result.Should().Contain("## PageRank");
        result.Should().Contain("| 1 |");
        result.Should().Contain("CoreHub");
        result.Should().Contain("0.8700");
        result.Should().Contain("BaseRepo");
        result.Should().Contain("transitive call-graph influence");
    }

    // ── GetBetweennessAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task GetBetweennessAsync_WhenGdsFails_ReturnsInstallGuidance()
    {
        var (sut, graph) = Build();
        graph.GetBetweennessAsync(null, Arg.Any<int>(), Arg.Any<CancellationToken>())
             .ThrowsAsync(new InvalidOperationException("No such procedure: gds.betweenness.stream"));

        var result = await sut.GetBetweennessAsync();

        result.Should().Contain("Betweenness Centrality failed");
        result.Should().Contain("Graph Data Science");
    }

    [Fact]
    public async Task GetBetweennessAsync_WithResults_ReturnsBridgeTable()
    {
        var (sut, graph) = Build();
        graph.GetBetweennessAsync(null, Arg.Any<int>(), Arg.Any<CancellationToken>())
             .Returns([(Node("b1", "EventBus", CodeNodeType.Class, "src/Bus.cs"), 1540.0),
                       (Node("b2", "Mediator", CodeNodeType.Class), 321.0)]);

        var result = await sut.GetBetweennessAsync();

        result.Should().Contain("## Betweenness Centrality");
        result.Should().Contain("Bridge Nodes");
        result.Should().Contain("EventBus");
        result.Should().Contain("1540");
        result.Should().Contain("connective tissue");
    }

    // ── FindNaturalModulesAsync ───────────────────────────────────────────────

    [Fact]
    public async Task FindBridgesAsync_WhenGdsFails_ReturnsInstallGuidance()
    {
        var (sut, graph) = Build();
        graph.GetBetweennessAsync(null, Arg.Any<int>(), Arg.Any<CancellationToken>())
             .ThrowsAsync(new InvalidOperationException("No such procedure: gds.betweenness.stream"));

        var result = await sut.FindBridgesAsync();

        result.Should().Contain("Bridge detection failed");
        result.Should().Contain("Graph Data Science");
    }

    [Fact]
    public async Task FindBridgesAsync_WithResults_ReturnsLayersRiskAndConfidence()
    {
        var (sut, graph) = Build();
        var bridge = Node(
            "b1",
            "PaymentFacade",
            CodeNodeType.Class,
            "src/Application/Payments/PaymentFacade.cs",
            line: 12,
            @namespace: "CodeMeridian.Application.Payments",
            project: "CodeMeridian",
            updatedAt: DateTimeOffset.UtcNow,
            sourceHash: "abc123");
        var apiCaller = Node("c1", "PaymentsController", CodeNodeType.Class, "src/Api/PaymentsController.cs", @namespace: "CodeMeridian.Api.Payments");
        var infraCallee = Node("d1", "StripeGateway", CodeNodeType.Class, "src/Infrastructure/Payments/StripeGateway.cs", @namespace: "CodeMeridian.Infrastructure.Payments");

        graph.GetBetweennessAsync(null, Arg.Any<int>(), Arg.Any<CancellationToken>())
             .Returns([(bridge, 980.0)]);
        graph.GetContextForEditingAsync("b1", Arg.Any<CancellationToken>())
             .Returns(new EditingContext(bridge, [apiCaller], [infraCallee], []));

        var result = await sut.FindBridgesAsync();

        result.Should().Contain("## Bridge Nodes");
        result.Should().Contain("PaymentFacade");
        result.Should().Contain("API, Application, Infrastructure");
        result.Should().Contain("high bridge risk across multiple layers");
        result.Should().Contain("| High |");
    }

    [Fact]
    public async Task FindNaturalModulesAsync_WhenGdsFails_ReturnsInstallGuidance()
    {
        var (sut, graph) = Build();
        graph.FindNaturalModulesAsync(null, Arg.Any<CancellationToken>())
             .ThrowsAsync(new InvalidOperationException("No such procedure: gds.louvain.stream"));

        var result = await sut.FindNaturalModulesAsync();

        result.Should().Contain("Community detection failed");
        result.Should().Contain("Graph Data Science");
    }

    [Fact]
    public async Task FindNaturalModulesAsync_WithResults_GroupsByCommunity()
    {
        var (sut, graph) = Build();
        graph.FindNaturalModulesAsync(null, Arg.Any<CancellationToken>())
             .Returns([
                 (Node("a1", "ServiceA", CodeNodeType.Class, "src/A.cs"), 1L),
                 (Node("a2", "ServiceB", CodeNodeType.Class, "src/B.cs"), 1L),
                 (Node("b1", "HelperX",  CodeNodeType.Class, "src/X.cs"), 2L),
             ]);

        var result = await sut.FindNaturalModulesAsync();

        result.Should().Contain("## Natural Modules (Louvain)");
        result.Should().Contain("**2** organic communities");
        result.Should().Contain("Community 1");
        result.Should().Contain("ServiceA");
        result.Should().Contain("ServiceB");
        result.Should().Contain("Community 2");
        result.Should().Contain("HelperX");
        result.Should().Contain("organic module boundaries");
    }

    [Fact]
    public async Task SuggestExtractionsAsync_WhenGdsFails_ReturnsInstallGuidance()
    {
        var (sut, graph) = Build();
        graph.FindNaturalModulesAsync(null, Arg.Any<CancellationToken>())
             .ThrowsAsync(new InvalidOperationException("No such procedure: gds.louvain.stream"));

        var result = await sut.SuggestExtractionsAsync();

        result.Should().Contain("Extraction suggestion failed");
        result.Should().Contain("Graph Data Science");
    }

    [Fact]
    public async Task SuggestExtractionsAsync_WithCommunitySignals_ReturnsExtractionCandidates()
    {
        var (sut, graph) = Build();
        var anchor = Node(
            "a1",
            "PaymentFacade",
            CodeNodeType.Class,
            "src/Application/Payments/PaymentFacade.cs",
            line: 12,
            project: "Shop",
            updatedAt: DateTimeOffset.UtcNow,
            lineCount: 340,
            sourceHash: "abc123",
            fileRole: IndexedFileRole.Source,
            @namespace: "Shop.Application.Payments");
        var helper = Node(
            "a2",
            "RetryPolicyBuilder",
            CodeNodeType.Class,
            "src/Application/Payments/RetryPolicyBuilder.cs",
            line: 8,
            project: "Shop",
            updatedAt: DateTimeOffset.UtcNow,
            lineCount: 90,
            sourceHash: "def456",
            fileRole: IndexedFileRole.Source,
            @namespace: "Shop.Application.Payments");
        var serializer = Node(
            "a3",
            "PaymentMapper",
            CodeNodeType.Method,
            "src/Application/Payments/PaymentMapper.cs",
            line: 20,
            project: "Shop",
            updatedAt: DateTimeOffset.UtcNow,
            lineCount: 30,
            sourceHash: "ghi789",
            fileRole: IndexedFileRole.Source,
            @namespace: "Shop.Application.Payments");
        var otherCommunity = Node(
            "b1",
            "EmailTemplateBuilder",
            CodeNodeType.Class,
            "src/Application/Notifications/EmailTemplateBuilder.cs",
            line: 6,
            project: "Shop",
            updatedAt: DateTimeOffset.UtcNow,
            lineCount: 40,
            sourceHash: "jkl012",
            fileRole: IndexedFileRole.Source,
            @namespace: "Shop.Application.Notifications");
        var test = Node("t1", "PaymentFacadeTests", CodeNodeType.Class, "tests/Payments/PaymentFacadeTests.cs", 5, "Shop", fileRole: IndexedFileRole.Test);

        graph.FindNaturalModulesAsync("Shop", Arg.Any<CancellationToken>())
            .Returns([
                (anchor, 1L),
                (helper, 1L),
                (serializer, 1L),
                (otherCommunity, 2L)
            ]);
        graph.FindHotspotsAsync("Shop", 50, Arg.Any<CancellationToken>())
            .Returns([(anchor, 6)]);
        graph.FindGodClassesAsync("Shop", 300, 3, Arg.Any<CancellationToken>())
            .Returns([(anchor, 340, 6)]);
        graph.FindCoverageGapsAsync("Shop", Arg.Any<CancellationToken>())
            .Returns([serializer]);
        graph.FindRelatedTestsAsync(anchor.Id, "Shop", Arg.Any<CancellationToken>())
            .Returns([(test, "direct")]);
        graph.FindRelatedTestsAsync(helper.Id, "Shop", Arg.Any<CancellationToken>())
            .Returns([]);
        graph.FindRelatedTestsAsync(serializer.Id, "Shop", Arg.Any<CancellationToken>())
            .Returns([]);

        var result = await sut.SuggestExtractionsAsync("Shop", limit: 5);

        result.Should().Contain("## Refactor Extraction Candidates - Shop");
        result.Should().Contain("`Shop.Application`");
        result.Should().Contain("PaymentFacade");
        result.Should().Contain("PaymentFacadeTests");
        result.Should().Contain("coverage gaps");
        result.Should().Contain("anchor fan-in 6");
        result.Should().Contain("large (340 lines)");
    }

    // ── FindSimilarToNodeAsync ────────────────────────────────────────────────

    [Fact]
    public async Task FindSimilarToNodeAsync_WhenNoEmbeddings_ReturnsEmbeddingGuidance()
    {
        var (sut, graph) = Build();
        graph.FindSimilarToNodeAsync("Method:Foo.Bar", null, 10, Arg.Any<CancellationToken>())
             .Returns([]);

        var result = await sut.FindSimilarToNodeAsync("Method:Foo.Bar");

        result.Should().Contain("No similar nodes found");
        result.Should().Contain("embeddingCsv");
        result.Should().Contain("ingest_code_node");
    }

    [Fact]
    public async Task FindSimilarToNodeAsync_WithResults_ReturnsSimilarityTable()
    {
        var (sut, graph) = Build();
        graph.FindSimilarToNodeAsync("Method:Foo.Bar", null, 10, Arg.Any<CancellationToken>())
             .Returns([(Node("s1", "SimilarMethod", CodeNodeType.Method, "src/Similar.cs"), 0.96),
                       (Node("s2", "RelatedHelper", CodeNodeType.Method), 0.74)]);

        var result = await sut.FindSimilarToNodeAsync("Method:Foo.Bar");

        result.Should().Contain("## Semantically Similar Nodes");
        result.Should().Contain("**2** nodes");
        result.Should().Contain("SimilarMethod");
        result.Should().Contain("96.0%");
        result.Should().Contain("RelatedHelper");
        result.Should().Contain("74.0%");
        result.Should().Contain("Semantic similarity");
    }

    [Fact]
    public async Task FindHybridSearchAsync_WithResults_UsesEmbeddingAndGraphConstraints()
    {
        var (sut, graph, embeddings) = BuildWithEmbeddings();
        embeddings.IsAvailableAsync(Arg.Any<CancellationToken>()).Returns(true);
        var expectedEmbedding = new[] { 0.1f, 0.2f, 0.3f, 0.4f };
        embeddings.GenerateEmbeddingAsync("retry policy", Arg.Any<CancellationToken>())
            .Returns(expectedEmbedding);
        graph.FindHybridMatchesAsync(
                Arg.Is<float[]>(embedding => embedding.SequenceEqual(expectedEmbedding)),
                "OrderService",
                3,
                "Shop",
                true,
                10,
                Arg.Any<CancellationToken>())
            .Returns([
                (Node("n1", "RetryPolicy", CodeNodeType.Class, "src/RetryPolicy.cs"), 0.94),
                (Node("n2", "BackoffHelper", CodeNodeType.Method, "src/BackoffHelper.cs"), 0.82)
            ]);

        var result = await sut.FindHybridSearchAsync("retry policy", nearNodeId: "OrderService", projectContext: "Shop");

        result.Should().Contain("## Hybrid Semantic Graph Search");
        result.Should().Contain("retry policy");
        result.Should().Contain("OrderService");
        result.Should().Contain("RetryPolicy");
        result.Should().Contain("94.0%");
        result.Should().Contain("BackoffHelper");
        await embeddings.Received(1).GenerateEmbeddingAsync("retry policy", Arg.Any<CancellationToken>());
        await graph.Received(1).FindHybridMatchesAsync(Arg.Any<float[]>(), "OrderService", 3, "Shop", true, 10, Arg.Any<CancellationToken>());
    }

    // ── FindDuplicateCandidatesAsync ─────────────────────────────────────────

    [Fact]
    public async Task FindDuplicateCandidatesAsync_WhenNoCandidates_ReturnsGuidance()
    {
        var (sut, graph) = Build();
        graph.FindDuplicateCandidatesAsync(null, null, null, 5, 0.88, true, 20, Arg.Any<CancellationToken>())
             .Returns([]);

        var result = await sut.FindDuplicateCandidatesAsync();

        result.Should().Contain("No duplicate-code candidates found");
        result.Should().Contain("Embeddings must be stored");
    }

    [Fact]
    public async Task FindDuplicateCandidatesAsync_WithCandidates_ReturnsWorkflowTable()
    {
        var (sut, graph) = Build();
        var source = Node("m1", "CalculateTotal", CodeNodeType.Method, "src/Orders.cs", line: 42, lineCount: 18);
        var candidate = Node("m2", "ComputeTotal", CodeNodeType.Method, "src/Billing.cs", line: 87, lineCount: 20);

        graph.FindDuplicateCandidatesAsync("Shop", "Domain", CodeNodeType.Method, 10, 0.90, true, 20, Arg.Any<CancellationToken>())
             .Returns([new DuplicateCandidate(source, candidate, 0.94, 1, 2, true, false)]);

        var result = await sut.FindDuplicateCandidatesAsync(
            projectContext: "Shop",
            namespaceFilter: "Domain",
            nodeType: "Method",
            minLineCount: 10,
            minSimilarity: 0.90);

        result.Should().Contain("## Duplicate-Code Candidates - Shop");
        result.Should().Contain("94.0%");
        result.Should().Contain("CalculateTotal");
        result.Should().Contain("ComputeTotal");
        result.Should().Contain("18/20 lines");
        result.Should().Contain("Medium (3 callers)");
        result.Should().Contain("source only");
    }

    [Fact]
    public async Task FindDuplicateCandidatesAsync_WithInvalidNodeType_ReturnsGuidance()
    {
        var (sut, _) = Build();

        var result = await sut.FindDuplicateCandidatesAsync(nodeType: "File");

        result.Should().Contain("Unknown duplicate candidate node type");
        result.Should().Contain("Method");
        result.Should().Contain("Class");
    }

    [Fact]
    public async Task BuildMinimalContextAsync_WithRelatedTests_RendersDirectAndHeuristicSections()
    {
        var (sut, graph) = Build();
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
        result.Should().Contain("Direct test callers:");
        result.Should().Contain("OrderServiceTests");
        result.Should().Contain("Heuristic matches:");
        result.Should().Contain("PlaceOrderTests");
        result.Should().Contain("heuristic");
        result.Should().Contain("**Estimated:**");
        result.Should().Contain("**Complexity:** Low");
        result.Should().Contain("Small or fast model likely sufficient");
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
        var (sut, graph) = Build();
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
        result.Should().Contain("### Degraded mode");
        result.Should().Contain("`context_pack_status=degraded`");
        result.Should().Contain("failed_step: `impact_analysis`");
        result.Should().Contain("exception: `InvalidOperationException`");
        result.Should().Contain("### Files likely needed");
    }

    [Fact]
    public async Task FindStaleKnowledgeAsync_WhenNoSignals_ReturnsGuidance()
    {
        var (sut, graph) = Build();
        var vector = Substitute.For<IVectorRepository>();
        sut = new CodebaseQueryService(graph, vector);

        vector
            .ListAsync(Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([
                new KnowledgeDocument
                {
                    Id = "doc-1",
                    Content = "This document contains general overview text.",
                    Source = "docs/overview.md",
                    ProjectContext = "Shop",
                    UpdatedAt = DateTimeOffset.UtcNow
                }
            ]);

        graph
            .QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>())
            .Returns([]);
        graph
            .FindUnreferencedAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns([]);
        graph
            .GetMostRecentCodeUpdateAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(DateTimeOffset.UtcNow);

        var result = await sut.FindStaleKnowledgeAsync("Shop");

        result.Should().Contain("No obvious stale knowledge found");
        result.Should().Contain("appear consistent");
    }

    [Fact]
    public async Task FindStaleKnowledgeAsync_WithMissingMentionAndOrphanedConcept_ReturnsFindings()
    {
        var (sut, graph) = Build();
        var vector = Substitute.For<IVectorRepository>();
        sut = new CodebaseQueryService(graph, vector);

        vector
            .ListAsync(Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([
                new KnowledgeDocument
                {
                    Id = "doc-adr-4",
                    Content = "ADR-004 references PaymentGateway.ChargeAsync",
                    Source = "ADR-004.md",
                    ProjectContext = "Shop",
                    UpdatedAt = DateTimeOffset.UtcNow.AddDays(-30),
                    Metadata = new Dictionary<string, string>
                    {
                        ["mentions"] = "Method:Shop.Payments.PaymentGateway.ChargeAsync"
                    }
                }
            ]);

        graph
            .GetContextForEditingAsync("Method:Shop.Payments.PaymentGateway.ChargeAsync", Arg.Any<CancellationToken>())
            .Returns(new EditingContext(null, [], [], []));
        graph
            .QueryNodesAsync(Arg.Is<CodeGraphQuery>(q => q.TypeFilter == CodeNodeType.ExternalConcept), Arg.Any<CancellationToken>())
            .Returns([new CodeNode { Id = "db:orders", Name = "orders table", Type = CodeNodeType.ExternalConcept, ProjectContext = "Shop" }]);
        graph
            .QueryEdgesAsync("db:orders", 1, Arg.Any<CancellationToken>())
            .Returns([]);
        graph
            .FindUnreferencedAsync("Shop", Arg.Any<CancellationToken>())
            .Returns([]);
        graph
            .GetMostRecentCodeUpdateAsync("Shop", Arg.Any<CancellationToken>())
            .Returns(DateTimeOffset.UtcNow);

        var result = await sut.FindStaleKnowledgeAsync("Shop");

        result.Should().Contain("## Stale Knowledge");
        result.Should().Contain("Unresolved documentation references");
        result.Should().Contain("ADR-004");
        result.Should().Contain("PaymentGateway.ChargeAsync");
        result.Should().Contain("Orphaned external concepts");
        result.Should().Contain("orders table");
    }

    [Fact]
    public async Task FindStaleKnowledgeAsync_WithGenericTechAndConfigMentions_DoesNotReportFalsePositives()
    {
        var (sut, graph) = Build();
        var vector = Substitute.For<IVectorRepository>();
        sut = new CodebaseQueryService(graph, vector);

        vector
            .ListAsync(Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([
                new KnowledgeDocument
                {
                    Id = "doc-indexer",
                    Content = "Configure TypeScript with meridian.json, mcp.json, config.toml. Example calls: axios.post, client.get, app.MapPost, api.example.com.",
                    Source = "tools/Indexer/README.md",
                    ProjectContext = "Shop",
                    UpdatedAt = DateTimeOffset.UtcNow
                }
            ]);

        graph
            .QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>())
            .Returns([]);
        graph
            .FindUnreferencedAsync("Shop", Arg.Any<CancellationToken>())
            .Returns([]);
        graph
            .GetMostRecentCodeUpdateAsync("Shop", Arg.Any<CancellationToken>())
            .Returns(DateTimeOffset.UtcNow);

        var result = await sut.FindStaleKnowledgeAsync("Shop");

        result.Should().Contain("No obvious stale knowledge found");
        result.Should().NotContain("axios.post");
        result.Should().NotContain("meridian.json");
        result.Should().NotContain("TypeScript");
    }

    [Fact]
    public async Task FindStaleKnowledgeAsync_WithConfiguredSkipPrefix_SkipsHeuristicMentionScanning()
    {
        var (sut, graph) = Build(new CodebaseAnalysisOptions
        {
            StaleKnowledge = new StaleKnowledgeOptions
            {
                SkipHeuristicSourcePrefixes = ["docs/custom/"]
            }
        });
        var vector = Substitute.For<IVectorRepository>();
        sut = new CodebaseQueryService(graph, vector, Options.Create(new CodebaseAnalysisOptions
        {
            StaleKnowledge = new StaleKnowledgeOptions
            {
                SkipHeuristicSourcePrefixes = ["docs/custom/"]
            }
        }));

        vector
            .ListAsync(Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([
                new KnowledgeDocument
                {
                    Id = "doc-custom",
                    Content = "MissingThingService should not be scanned because this source prefix is configured as planning/example docs.",
                    Source = "docs/custom/example.md",
                    ProjectContext = "Shop",
                    UpdatedAt = DateTimeOffset.UtcNow
                }
            ]);

        graph
            .QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>())
            .Returns([]);
        graph
            .FindUnreferencedAsync("Shop", Arg.Any<CancellationToken>())
            .Returns([]);
        graph
            .GetMostRecentCodeUpdateAsync("Shop", Arg.Any<CancellationToken>())
            .Returns(DateTimeOffset.UtcNow);

        var result = await sut.FindStaleKnowledgeAsync("Shop");

        result.Should().Contain("No obvious stale knowledge found");
        result.Should().NotContain("MissingThingService");
    }

    [Fact]
    public async Task FindImplementationSurfaceAsync_WithMatchingNodes_RanksLikelyFiles()
    {
        var (sut, graph) = Build();
        graph
            .QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>())
            .Returns([
                Node("m1", "FindStaleKnowledgeAsync", CodeNodeType.Method, "TODO.md", 1, "CodeMeridian"),
                Node("c1", "CodebaseQueryService", CodeNodeType.Class, "src/Application/Services/CodebaseQueryService.Analytics.cs", 1, "CodeMeridian")
            ]);

        var result = await sut.FindImplementationSurfaceAsync(
            "add stale knowledge query",
            "stale,knowledge",
            "CodeMeridian");

        result.Should().Contain("## Implementation Surface");
        result.Should().Contain("TODO.md");
        result.Should().Contain("FindStaleKnowledgeAsync");
        result.Should().Contain("Target confidence");
        result.Should().Contain("exact");
        result.Should().Contain("`m1`");
        result.Should().Contain("Freshness");
    }

    [Fact]
    public async Task AnalyzeFeatureImplementationPathAsync_WithDocsCodeAndTests_ReturnsEvidenceAndRisk()
    {
        var graph = Substitute.For<ICodeGraphRepository>();
        var vector = Substitute.For<IVectorRepository>();
        var sut = new CodebaseQueryService(graph, vector);
        var service = Node(
            "s1",
            "FeatureImplementationAnalysisService",
            CodeNodeType.Class,
            "src/Application/Services/FeatureImplementationAnalysisService.cs",
            12,
            "CodeMeridian",
            updatedAt: DateTimeOffset.UtcNow,
            lineCount: 60,
            sourceHash: "abc",
            summary: "Maps feature docs to likely implementation surfaces.");
        var tool = Node(
            "m1",
            "AnalyzeFeatureImplementationPathAsync",
            CodeNodeType.Method,
            "src/McpServer/Tools/CodebaseTools.cs",
            120,
            "CodeMeridian",
            updatedAt: DateTimeOffset.UtcNow,
            lineCount: 25,
            sourceHash: "def",
            summary: "MCP tool exposure for feature implementation path analysis.");
        var test = Node(
            "t1",
            "FeatureImplementationAnalysisServiceTests",
            CodeNodeType.Class,
            "tests/CodeMeridian.Application.Tests/Services/FeatureImplementationAnalysisServiceTests.cs",
            5,
            "CodeMeridian",
            updatedAt: DateTimeOffset.UtcNow,
            lineCount: 40,
            sourceHash: "ghi",
            fileRole: IndexedFileRole.Test);

        vector
            .SearchByTextAsync("Add Feature Implementation Path", "CodeMeridian", 6, Arg.Any<CancellationToken>())
            .Returns([
                new KnowledgeDocument
                {
                    Id = "feature-1",
                    Content = """
                              # Add Feature Implementation Path
                              - Status: pending
                              This feature maps feature docs to implementation surfaces and test seams.
                              """,
                    Source = "docs/features/39-add-feature-implementation-path.md",
                    ProjectContext = "CodeMeridian"
                }
            ]);
        graph
            .QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>())
            .Returns([service, tool]);
        graph
            .FindRelatedTestsAsync(service.Id, "CodeMeridian", Arg.Any<CancellationToken>())
            .Returns([(test, "direct")]);
        graph
            .FindRelatedTestsAsync(tool.Id, "CodeMeridian", Arg.Any<CancellationToken>())
            .Returns([]);

        var result = await sut.AnalyzeFeatureImplementationPathAsync(
            "Add Feature Implementation Path",
            "CodeMeridian");

        result.Should().Contain("## Feature Implementation Path");
        result.Should().Contain("documented_with_code_and_test_evidence");
        result.Should().Contain("Confidence:** high");
        result.Should().Contain("FeatureImplementationAnalysisService");
        result.Should().Contain("Presentation/MCP");
        result.Should().Contain("FeatureImplementationAnalysisServiceTests");
        result.Should().Contain("docs/features/39-add-feature-implementation-path.md");
        result.Should().Contain("Risk level");
    }

    [Fact]
    public async Task AnalyzeFeatureImplementationPathAsync_WhenNoEvidence_ReturnsMissingGraphEvidence()
    {
        var graph = Substitute.For<ICodeGraphRepository>();
        var vector = Substitute.For<IVectorRepository>();
        var sut = new CodebaseQueryService(graph, vector);

        vector
            .SearchByTextAsync("Add Ghost Feature", "CodeMeridian", 6, Arg.Any<CancellationToken>())
            .Returns([]);
        graph
            .QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var result = await sut.AnalyzeFeatureImplementationPathAsync(
            "Add Ghost Feature",
            "CodeMeridian");

        result.Should().Contain("not_found_in_graph");
        result.Should().Contain("Confidence:** low");
        result.Should().Contain("No matching KnowledgeDocument");
        result.Should().Contain("No CodeNode implementation surface matched");
        result.Should().Contain("No related test nodes were linked");
        result.Should().Contain("Risk level:** unknown");
    }

    [Fact]
    public async Task PlanEditRouteAsync_WithGraphMatches_ReturnsOrderedItinerary()
    {
        var (sut, graph) = Build();
        var port = Node("i1", "IPaymentRepository", CodeNodeType.Interface, "src/Application/Ports/IPaymentRepository.cs", 1, "Shop");
        var service = Node("s1", "PaymentService", CodeNodeType.Class, "src/Application/Payments/PaymentService.cs", 12, "Shop");
        var implementation = Node("r1", "SqlPaymentRepository", CodeNodeType.Class, "src/Infrastructure/Payments/SqlPaymentRepository.cs", 9, "Shop");
        var endpoint = Node("e1", "PaymentEndpoint", CodeNodeType.Method, "src/McpServer/Api/PaymentEndpoint.cs", 20, "Shop");
        var program = Node("p1", "Program", CodeNodeType.File, "src/McpServer/Program.cs", 1, "Shop");
        var test = Node("t1", "PaymentServiceTests", CodeNodeType.Class, "tests/Shop.Tests/PaymentServiceTests.cs", 8, "Shop");

        graph
            .QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>())
            .Returns([service, port, implementation, endpoint, program]);
        graph
            .GetContextForEditingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new EditingContext(service, [endpoint], [implementation], [port]));
        graph
            .FindImpactAsync(Arg.Any<string>(), 2, Arg.Any<CancellationToken>())
            .Returns([(endpoint, 1)]);
        graph
            .FindDownstreamAsync(Arg.Any<string>(), 2, Arg.Any<CancellationToken>())
            .Returns([(implementation, 1), (program, 2)]);
        graph
            .FindRelatedTestsAsync(Arg.Any<string>(), "Shop", Arg.Any<CancellationToken>())
            .Returns([(test, "direct")]);

        var result = await sut.PlanEditRouteAsync(
            "replace repository pattern in payments",
            "repository,payments",
            "Shop");

        result.Should().Contain("## Change Route");
        result.Should().Contain("Contract / port");
        result.Should().Contain("Application / domain behavior");
        result.Should().Contain("Infrastructure implementation");
        result.Should().Contain("Composition and API entry points");
        result.Should().Contain("Tests and verification");
        result.Should().Contain("IPaymentRepository");
        result.Should().Contain("PaymentService");
        result.Should().Contain("SqlPaymentRepository");
        result.Should().Contain("PaymentEndpoint");
        result.Should().Contain("PaymentServiceTests");
        result.Should().Contain("Graph signals");
    }

    [Fact]
    public async Task PlanEditRouteAsync_WhenNoMatches_ReturnsGuidance()
    {
        var (sut, graph) = Build();
        graph
            .QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var result = await sut.PlanEditRouteAsync("replace repository pattern in payments");

        result.Should().Contain("No edit route found");
        result.Should().Contain("find_implementation_surface");
    }

    [Fact]
    public async Task ReplaceSurfaceAsync_GroupsSafeAndRiskyReplacementClusters()
    {
        var (sut, graph) = Build();
        var safeNode = Node(
            "m-safe",
            "OrderJsonSerializer.Serialize",
            CodeNodeType.Method,
            "src/Application/Orders/OrderJsonSerializer.cs",
            18,
            "Shop",
            updatedAt: DateTimeOffset.UtcNow,
            lineCount: 20,
            summary: "Uses Newtonsoft.Json to serialize outbound order payloads.",
            fileRole: IndexedFileRole.Source,
            @namespace: "Shop.Application.Orders");
        var riskyNode = Node(
            "m-risky",
            "OrdersController.Post",
            CodeNodeType.Method,
            "src/Api/OrdersController.cs",
            26,
            "Shop",
            updatedAt: DateTimeOffset.UtcNow,
            lineCount: 30,
            summary: "Maps API requests with Newtonsoft.Json settings before dispatch.",
            fileRole: IndexedFileRole.Source,
            @namespace: "Shop.Api.Orders");
        var safeTest = Node("t-safe", "OrderJsonSerializerTests", CodeNodeType.Class, "tests/Orders/OrderJsonSerializerTests.cs", 5, "Shop", fileRole: IndexedFileRole.Test);
        var diagnostic = Node("d1", "CS8602", CodeNodeType.Diagnostic, "src/Api/OrdersController.cs", 28, "Shop");
        var endpoint = Node("e1", "POST /api/orders", CodeNodeType.ApiEndpoint, "src/Api/OrdersEndpoints.cs", 10, "Shop");

        graph
            .QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>())
            .Returns([safeNode, riskyNode]);
        graph
            .FindRelatedTestsAsync(safeNode.Id, "Shop", Arg.Any<CancellationToken>())
            .Returns([(safeTest, "direct")]);
        graph
            .FindRelatedTestsAsync(riskyNode.Id, "Shop", Arg.Any<CancellationToken>())
            .Returns([]);
        graph
            .FindDiagnosticsForNodeAsync(safeNode.Id, Arg.Any<CancellationToken>())
            .Returns([]);
        graph
            .FindDiagnosticsForNodeAsync(riskyNode.Id, Arg.Any<CancellationToken>())
            .Returns([diagnostic]);
        graph
            .GetContextForEditingAsync(safeNode.Id, Arg.Any<CancellationToken>())
            .Returns(new EditingContext(safeNode, [], [], []));
        graph
            .GetContextForEditingAsync(riskyNode.Id, Arg.Any<CancellationToken>())
            .Returns(new EditingContext(riskyNode, [endpoint], [], []));

        var result = await sut.ReplaceSurfaceAsync("Newtonsoft.Json", "System.Text.Json", "Shop");

        result.Should().Contain("## Replacement Surface - `Newtonsoft.Json` -> `System.Text.Json`");
        result.Should().Contain("### Safe replacement groups (1)");
        result.Should().Contain("Shop.Application");
        result.Should().Contain("OrderJsonSerializer.Serialize");
        result.Should().Contain("OrderJsonSerializerTests");
        result.Should().Contain("swap to `System.Text.Json` inside one module");
        result.Should().Contain("### Risky replacement groups (1)");
        result.Should().Contain("Shop.Api");
        result.Should().Contain("OrdersController.Post");
        result.Should().Contain("crosses API boundary");
        result.Should().Contain("no related tests");
    }

    [Fact]
    public async Task ResolveExactSymbolAsync_WithFileAndLineHints_ReturnsCanonicalNodeIds()
    {
        var (sut, graph) = Build();
        var target = Node(
            "Method:CodeMeridian.Application.Services.CodebaseQueryService.FindImplementationSurfaceAsync",
            "FindImplementationSurfaceAsync",
            CodeNodeType.Method,
            "src/Application/Services/CodebaseQueryService.Surface.cs",
            8,
            "CodeMeridian",
            updatedAt: DateTimeOffset.UtcNow,
            lineCount: 40);

        graph
            .QueryNodesAsync(
                Arg.Is<CodeGraphQuery>(q =>
                    q.NameFilter == "FindImplementationSurfaceAsync"
                    && q.FilePathFilter == "src/Application/Services/CodebaseQueryService.Surface.cs"
                    && q.ProjectContext == "CodeMeridian"),
                Arg.Any<CancellationToken>())
            .Returns([target]);

        var result = await sut.ResolveExactSymbolAsync(
            "FindImplementationSurfaceAsync",
            "src/Application/Services/CodebaseQueryService.Surface.cs",
            line: 10,
            projectContext: "CodeMeridian");

        result.Should().Contain("## Exact Symbol Resolution");
        result.Should().Contain("**Confidence summary:** 1 exact");
        result.Should().Contain("Method:CodeMeridian.Application.Services.CodebaseQueryService.FindImplementationSurfaceAsync");
        result.Should().Contain("name/id match");
        result.Should().Contain("file match");
        result.Should().Contain("near line hint");
    }

    [Fact]
    public async Task CheckGraphFreshnessAsync_ReturnsConfidenceSignals()
    {
        var (sut, graph) = Build();
        graph
            .QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>())
            .Returns([
                Node("n1", "Roadmap", CodeNodeType.File, "TODO.md", 1, "CodeMeridian", updatedAt: DateTimeOffset.UtcNow, lineCount: 120, sourceHash: "abc123"),
                Node("n2", "Incomplete", CodeNodeType.Class, "src/File.cs", project: "CodeMeridian", updatedAt: DateTimeOffset.UtcNow)
            ]);

        var result = await sut.CheckGraphFreshnessAsync(projectContext: "CodeMeridian");

        result.Should().Contain("## Graph Freshness");
        result.Should().Contain("Trust summary");
        result.Should().Contain("High");
        result.Should().Contain("Medium");
        result.Should().Contain("Source verification");
        result.Should().Contain("checksum indexed");
        result.Should().Contain("missing source hash");
    }

    [Fact]
    public async Task CheckGraphFreshnessAsync_WhenProjectContextHasNoNodes_SuggestsClosestProject()
    {
        var (sut, graph) = Build();
        graph
            .QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>())
            .Returns([]);
        graph
            .GetProjectContextsAsync("code-meridian", Arg.Any<CancellationToken>())
            .Returns(["CodeMeridian"]);

        var result = await sut.CheckGraphFreshnessAsync(projectContext: "code-meridian");

        result.Should().Contain("No graph nodes found in 'code-meridian'");
        result.Should().Contain("Did you mean 'CodeMeridian'?");
    }

    [Fact]
    public async Task FindGraphDriftAsync_WhenProjectContextHasTypo_SuggestsClosestProject()
    {
        var (sut, graph) = Build();
        graph
            .QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>())
            .Returns([]);
        graph
            .GetProjectContextsAsync("code3meridian", Arg.Any<CancellationToken>())
            .Returns(["CodeMeridian"]);

        var result = await sut.FindGraphDriftAsync("code3meridian");

        result.Should().Contain("No graph nodes found in 'code3meridian'");
        result.Should().Contain("Did you mean 'CodeMeridian'?");
        result.Should().Contain("Run the indexer");
    }

    [Fact]
    public async Task FindGraphDriftAsync_WhenPrefilterFindsNoProjects_FallsBackToAllProjects()
    {
        var (sut, graph) = Build();
        graph
            .QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>())
            .Returns([]);
        graph
            .GetProjectContextsAsync("xode-meridian", Arg.Any<CancellationToken>())
            .Returns([]);
        graph
            .GetProjectContextsAsync(null, Arg.Any<CancellationToken>())
            .Returns(["CodeMeridian"]);

        var result = await sut.FindGraphDriftAsync("xode-meridian");

        result.Should().Contain("Did you mean 'CodeMeridian'?");
    }

    [Fact]
    public async Task GetArchitectureWeatherReportAsync_ReturnsSummarySignals()
    {
        var (sut, graph) = Build();
        graph.CountCodeNodesAsync("Shop", Arg.Any<CancellationToken>()).Returns(120);
        graph.CountCallEdgesAsync("Shop", Arg.Any<CancellationToken>()).Returns(340);
        graph.FindCyclesAsync("Shop", Arg.Any<CancellationToken>())
            .Returns([("Shop.Core", "Shop.Infrastructure")]);
        graph.FindArchitectureViolationsAsync("Shop", Arg.Any<CancellationToken>())
            .Returns([(Node("source", "Repository", CodeNodeType.Class, "src/Repository.cs"), Node("target", "Domain", CodeNodeType.Class, "src/Domain.cs"), "Infrastructure -> Core")]);
        graph.FindCoverageGapsAsync("Shop", Arg.Any<CancellationToken>())
            .Returns([Node("gap", "PlaceOrder", CodeNodeType.Method, "src/OrderService.cs", project: "Shop")]);
        graph.GetBetweennessAsync("Shop", 10, Arg.Any<CancellationToken>())
            .Returns([(Node("bridge", "CheckoutFacade", CodeNodeType.Class, "src/CheckoutFacade.cs", project: "Shop"), 0.42)]);
        graph.QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>())
            .Returns([
                Node("fresh", "Fresh", CodeNodeType.Class, "src/Fresh.cs", 1, "Shop", updatedAt: DateTimeOffset.UtcNow, lineCount: 12, sourceHash: "abc"),
                Node("stale", "Stale", CodeNodeType.Class, project: "Shop")
            ]);

        var result = await sut.GetArchitectureWeatherReportAsync("Shop");

        result.Should().Contain("# Architecture Weather Report - Shop");
        result.Should().Contain("**Weather:**");
        result.Should().Contain("Code nodes");
        result.Should().Contain("Call relationships");
        result.Should().Contain("Namespace cycles");
        result.Should().Contain("Architecture violations");
        result.Should().Contain("Bridge nodes");
        result.Should().Contain("Untested methods/classes");
        result.Should().Contain("Low-confidence freshness nodes");
        result.Should().Contain("CheckoutFacade");
    }

    [Fact]
    public async Task FindGraphDriftAsync_WithIncompleteIndexedMetadata_ReturnsRecommendation()
    {
        var (sut, graph) = Build();
        graph
            .QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>())
            .Returns([
                Node("n1", "ServiceWithoutPath", CodeNodeType.Class, project: "CodeMeridian"),
                Node("n2", "MethodWithoutLine", CodeNodeType.Method, "src/Service.cs", project: "CodeMeridian", updatedAt: DateTimeOffset.UtcNow),
                Node("n3", "CodeMeridian.Services", CodeNodeType.Namespace, "src/Service.cs", 1, "CodeMeridian", updatedAt: DateTimeOffset.UtcNow)
            ]);

        var result = await sut.FindGraphDriftAsync("CodeMeridian");

        result.Should().Contain("## Graph Drift");
        result.Should().Contain("Source verification");
        result.Should().Contain("Missing file metadata");
        result.Should().Contain("Missing source hashes");
        result.Should().Contain("ServiceWithoutPath");
        result.Should().NotContain("CodeMeridian.Services");
        result.Should().Contain("codemeridian index");
    }
}
