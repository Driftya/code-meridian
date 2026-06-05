using CodeMeridian.Application.Services;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;
using FluentAssertions;
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

    private static CodeNode Node(
        string id,
        string name,
        CodeNodeType type,
        string? file = null,
        int? line = null,
        string? project = null,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? updatedAt = null,
        int? lineCount = null) => new()
    {
        Id = id,
        Name = name,
        Type = type,
        FilePath = file,
        LineNumber = line,
        LineCount = lineCount,
        ProjectContext = project,
        CreatedAt = createdAt,
        UpdatedAt = updatedAt
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
        result.Should().Contain("`UntouchedService`");
        result.Should().Contain("`OrphanMethod`");
        result.Should().Contain(":10");
        result.Should().Contain("heuristic");
    }

    // ── FindRecentlyChangedAsync ──────────────────────────────────────────────

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
    public async Task FindArchitectureViolationsAsync_WhenNoViolations_ReturnsCleanMessage()
    {
        var (sut, graph) = Build();
        graph.FindArchitectureViolationsAsync(null, Arg.Any<CancellationToken>())
             .Returns([]);

        var result = await sut.FindArchitectureViolationsAsync();

        result.Should().Contain("No Clean Architecture violations found");
        result.Should().Contain("Core and Application layers");
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
        result.Should().Contain("**1** edges break");
        result.Should().Contain("CoreService");
        result.Should().Contain("DbContext");
        result.Should().Contain("Core → MyApp.Infrastructure");
        result.Should().Contain("Rules:");
    }

    // ── FindHighChurnAsync ────────────────────────────────────────────────────

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
        result.Should().Contain("HotFile");
        result.Should().Contain("12×");
        result.Should().Contain("5×");
        result.Should().Contain("find_hotspots");
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
        result.Should().Contain("Confidence");
        result.Should().Contain("Freshness");
    }

    [Fact]
    public async Task CheckGraphFreshnessAsync_ReturnsConfidenceSignals()
    {
        var (sut, graph) = Build();
        graph
            .QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>())
            .Returns([
                Node("n1", "Roadmap", CodeNodeType.File, "TODO.md", 1, "CodeMeridian", updatedAt: DateTimeOffset.UtcNow),
                Node("n2", "Missing", CodeNodeType.Class, "missing/File.cs", 10, "CodeMeridian")
            ]);

        var result = await sut.CheckGraphFreshnessAsync(projectContext: "CodeMeridian");

        result.Should().Contain("## Graph Freshness");
        result.Should().Contain("Trust summary");
        result.Should().Contain("High");
        result.Should().Contain("Low");
        result.Should().Contain("File exists");
    }

    [Fact]
    public async Task FindGraphDriftAsync_WithMissingFiles_ReturnsRecommendation()
    {
        var (sut, graph) = Build();
        graph
            .QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>())
            .Returns([
                Node("n1", "MissingService", CodeNodeType.Class, "missing/Service.cs", 12, "CodeMeridian"),
                Node("n2", "MissingMethod", CodeNodeType.Method, "missing/Service.cs", 20, "CodeMeridian")
            ]);

        var result = await sut.FindGraphDriftAsync("CodeMeridian");

        result.Should().Contain("## Graph Drift");
        result.Should().Contain("missing files");
        result.Should().Contain("MissingService");
        result.Should().Contain("codemeridian index");
    }
}
