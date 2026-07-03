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

    private static CodeNode CreateFrontendStyleDeclaration(
        string id,
        string selectorText,
        string filePath,
        int lineNumber,
        string propertyName,
        string rawValue,
        IDictionary<string, string>? extraProperties = null,
        string project = "Shop.Web")
    {
        var properties = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["externalKind"] = "CssDeclaration",
            ["selectorText"] = selectorText,
            ["propertyName"] = propertyName,
            ["rawValue"] = rawValue
        };

        if (extraProperties is not null)
        {
            foreach (var pair in extraProperties)
                properties[pair.Key] = pair.Value;
        }

        return new CodeNode
        {
            Id = id,
            Name = $"{propertyName}: {rawValue}",
            Type = CodeNodeType.ExternalConcept,
            FilePath = filePath,
            LineNumber = lineNumber,
            LineCount = 1,
            ProjectContext = project,
            Properties = properties
        };
    }

    private static string WritePrecisionFeedbackFile(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"codemeridian-precision-feedback-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, content);
        return path;
    }

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
        result.Should().Contain("node is missing update metadata");
    }

    [Fact]
    public async Task FindImpactAsync_WithConfidenceSummary_ReturnsConfidenceCounts()
    {
        var (sut, graph) = Build();
        var target = Node("target", "ChargeAsync", CodeNodeType.Method, "src/Payments/PaymentGateway.cs", 42, "Shop", updatedAt: DateTimeOffset.UtcNow);
        var provenCaller = Node("caller-1", "CheckoutService.PlaceOrder", CodeNodeType.Method, "src/Checkout/CheckoutService.cs", 18, "Shop", updatedAt: DateTimeOffset.UtcNow);
        var heuristicCaller = Node("route-1", "POST /api/payments", CodeNodeType.ApiEndpoint, "src/Api/PaymentsEndpoint.cs", 9, "Shop.Api", updatedAt: DateTimeOffset.UtcNow);

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
                     heuristicCaller,
                     2,
                     [
                         new GraphPathStep(heuristicCaller, "Calls", null),
                         new GraphPathStep(target, null, null)
                     ])
             ]);

        var result = await sut.FindImpactAsync(
            "Method:Payments.PaymentGateway.ChargeAsync",
            depth: 3,
            detailLevel: ContextDetailLevel.Summary,
            includeConfidence: true);

        result.Should().Be(
            "Impact summary for `Method:Payments.PaymentGateway.ChargeAsync`: 2 affected code elements within 3 hops. " +
            "Confidence: Medium. 1 proven, 1 heuristic, 0 unknown risk.");
    }

    [Fact]
    public async Task FindImpactAsync_WithConfidence_WhenNoCallers_ReturnsGuidanceMessage()
    {
        var (sut, graph) = Build();
        graph.FindImpactPathsAsync("Method:Foo.Bar()", 5, Arg.Any<CancellationToken>())
             .Returns([]);

        var result = await sut.FindImpactAsync("Method:Foo.Bar()", includeConfidence: true);

        result.Should().Be(
            "No callers found for `Method:Foo.Bar()` within 5 hops. " +
            "The node may not exist in the graph or has no inbound dependencies.");
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
        result.Should().Contain("### Production candidates (2)");
        result.Should().Contain("CoreService");
        result.Should().Contain("42");
        result.Should().Contain("UtilHelper");
        result.Should().Contain("17");
        result.Should().Contain("| 1 |"); // rank 1
        result.Should().Contain("| 2 |"); // rank 2
    }

    [Fact]
    public async Task FindHotspotsAsync_DefaultNoiseReduction_HidesBroaderAndSuppressedResults()
    {
        var (sut, graph) = Build();
        graph.FindHotspotsAsync(null, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([
                (Node("prod-1", "BillingService", CodeNodeType.Class, "src/BillingService.cs", fileRole: IndexedFileRole.Source), 11),
                (Node("heur-1", "Orders", CodeNodeType.Namespace, "src/Orders", fileRole: IndexedFileRole.Source), 9),
                (Node("noise-1", "Orders:Timeout", CodeNodeType.ConfigurationKey, "appsettings.json", fileRole: IndexedFileRole.Configuration), 15)
            ]);

        var result = await sut.FindHotspotsAsync();

        result.Should().Contain("### Production candidates (1)");
        result.Should().Contain("BillingService");
        result.Should().Contain("Hidden by default: 1 broader heuristic match, 1 suppressed noise node.");
        result.Should().NotContain("### Broader heuristic matches");
        result.Should().NotContain("### Suppressed noise");
        result.Should().NotContain("Orders:Timeout");
    }

    [Fact]
    public async Task FindHotspotsAsync_WhenBroaderOutputEnabled_ShowsHeuristicAndSuppressedSections()
    {
        var (sut, graph) = Build(new CodebaseAnalysisOptions
        {
            Ranking = new RankingOptions
            {
                ProductionOnlyByDefault = false,
                IncludeSuppressedNoise = true
            }
        });
        graph.FindHotspotsAsync(null, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([
                (Node("prod-1", "BillingService", CodeNodeType.Class, "src/BillingService.cs", fileRole: IndexedFileRole.Source), 11),
                (Node("heur-1", "Orders", CodeNodeType.Namespace, "src/Orders", fileRole: IndexedFileRole.Source), 9),
                (Node("noise-1", "Orders:Timeout", CodeNodeType.ConfigurationKey, "appsettings.json", fileRole: IndexedFileRole.Configuration), 15)
            ]);

        var result = await sut.FindHotspotsAsync();

        result.Should().Contain("### Broader heuristic matches (1)");
        result.Should().Contain("### Suppressed noise (1)");
        result.Should().Contain("Orders");
        result.Should().Contain("Orders:Timeout");
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
    public async Task FindConnectionAsync_WithFrontendPath_SummarizesFrontendSignals()
    {
        var (sut, graph) = Build();
        graph.FindConnectionAsync("component", "style", Arg.Any<CancellationToken>())
             .Returns([
                 (Node("component", "HeroCard", CodeNodeType.File, "src/web/HeroCard.tsx", project: "Shop.Web"), (string?)null),
                 (new CodeNode
                 {
                     Id = "hero-class",
                     Name = "hero",
                     Type = CodeNodeType.ExternalConcept,
                     ProjectContext = "Shop.Web"
                 }, "UsesClass"),
                 (new CodeNode
                 {
                     Id = "hero-selector",
                     Name = ".hero",
                     Type = CodeNodeType.ExternalConcept,
                     ProjectContext = "Shop.Web"
                 }, "UsesClass"),
                 (Node("style", "HeroCard.scss", CodeNodeType.File, "src/web/HeroCard.scss", project: "Shop.Web"), "DefinesSelector")
             ]);

        var result = await sut.FindConnectionAsync("component", "style");

        result.Should().Contain("HeroCard");
        result.Should().Contain("UsesClass");
        result.Should().Contain("DefinesSelector");
        result.Should().Contain("Frontend signals: class usage, selector definition.");
    }

    [Fact]
    public async Task FindConnectionAsync_WithDatabasePath_ListsDatabaseOperationAndTableHop()
    {
        var (sut, graph) = Build();
        graph.FindConnectionAsync("endpoint", "table", Arg.Any<CancellationToken>())
             .Returns([
                 (Node("endpoint", "POST /api/orders", CodeNodeType.ApiEndpoint, project: "Shop.Api"), (string?)null),
                 (Node("handler", "CreateOrder", CodeNodeType.Method, "src/Api/OrdersEndpoint.cs", 22, "Shop.Api"), "Uses"),
                 (new CodeNode
                 {
                     Id = "db-op",
                     Name = "EFCore Writes Orders",
                     Type = CodeNodeType.ExternalConcept,
                     ProjectContext = "Shop.Api",
                     Properties = new Dictionary<string, string>(StringComparer.Ordinal)
                     {
                         ["externalKind"] = "DatabaseOperation",
                         ["provider"] = "EFCore"
                     }
                 }, "Writes"),
                 (Node("table", "Orders", CodeNodeType.DatabaseTable, project: "Shop.Api"), "Writes")
             ]);

        var result = await sut.FindConnectionAsync("endpoint", "table");

        result.Should().Contain("POST /api/orders");
        result.Should().Contain("CreateOrder");
        result.Should().Contain("EFCore Writes Orders");
        result.Should().Contain("**DatabaseTable** `Orders`");
        result.Should().Contain("—[Writes]→");
    }

    [Fact]
    public async Task TraceEndpointAsync_WhenNoPaths_ReturnsReindexHint()
    {
        var (sut, graph) = Build();
        graph.FindEndpointTracesAsync("POST /api/orders", "Shop.Api", 10, Arg.Any<CancellationToken>())
            .Returns([]);

        var result = await sut.TraceEndpointAsync("POST /api/orders", "Shop.Api");

        result.Should().Contain("No database or event trace found");
        result.Should().Contain(".meridian/database-tracing.json");
    }

    [Fact]
    public async Task TraceEndpointAsync_WithDatabaseAndEventPaths_GroupsResults()
    {
        var (sut, graph) = Build();
        var endpoint = Node("endpoint", "POST /api/orders", CodeNodeType.ApiEndpoint, project: "Shop.Api");
        var handler = Node("handler", "CreateOrder", CodeNodeType.Method, "src/Api/OrdersEndpoint.cs", 22, "Shop.Api");
        var operation = new CodeNode
        {
            Id = "db-op",
            Name = "EFCore Writes Orders",
            Type = CodeNodeType.ExternalConcept,
            ProjectContext = "Shop.Api",
            Properties = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["externalKind"] = "DatabaseOperation",
                ["provider"] = "EFCore"
            }
        };
        var table = Node("table", "Orders", CodeNodeType.DatabaseTable, project: "Shop.Api");
        var topic = Node("topic", "order-created", CodeNodeType.MessageTopic, project: "Shop.Api");

        graph.FindEndpointTracesAsync("POST /api/orders", "Shop.Api", 10, Arg.Any<CancellationToken>())
            .Returns([
                new EndpointTracePath([
                    new GraphPathStep(endpoint, "Uses", null),
                    new GraphPathStep(handler, "Writes", null),
                    new GraphPathStep(operation, "Writes", null),
                    new GraphPathStep(table, null, null)
                ]),
                new EndpointTracePath([
                    new GraphPathStep(endpoint, "Uses", null),
                    new GraphPathStep(handler, "PublishesTo", null),
                    new GraphPathStep(topic, null, null)
                ])
            ]);

        var result = await sut.TraceEndpointAsync("POST /api/orders", "Shop.Api");

        result.Should().Contain("## Endpoint Trace - `POST /api/orders` - Shop.Api");
        result.Should().Contain("### Database paths (1)");
        result.Should().Contain("### Event paths (1)");
        result.Should().Contain("EFCore Writes Orders");
        result.Should().Contain("**DatabaseTable** `Orders`");
        result.Should().Contain("**MessageTopic** `order-created`");
        result.Should().Contain("Graph-only trace");
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
    public async Task FindTestShieldAsync_WithSinglePrimaryCandidate_SuggestsFocusedCommand()
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
        result.Should().Contain("`dotnet test --filter FullyQualifiedName~OrdersEndpointTests`");
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
        result.Should().Contain("### Caller summary — showing 1 of 1 callers");
        result.Should().Contain("### Direct method callers (1)");
        result.Should().Contain("`OrderController.Create`");
        result.Should().Contain("src/Ctrl.cs");
        result.Should().Contain(":10");
        result.Should().Contain("direct production caller");
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

    [Fact]
    public async Task GetContextForEditingAsync_PrunesDuplicateFileOnlyCallers_AndGroupsCallerSections()
    {
        var (sut, graph) = Build();
        var target = Node("t1", "ChainService", CodeNodeType.Class, "src/Application/ChainService.cs", 12);
        var methodCaller = Node("m1", "ModerationWorkflow.Handle", CodeNodeType.Method, "src/Application/ModerationWorkflow.cs", 28);
        var classCaller = Node("c1", "ModerationWorkflow", CodeNodeType.Class, "src/Application/ModerationWorkflow.cs", 5);
        var duplicateFileCaller = Node("f1", "ModerationWorkflow.cs", CodeNodeType.File, "src/Application/ModerationWorkflow.cs", 1);
        var routeCaller = Node("r1", "POST /chains/{id}/moderate", CodeNodeType.ApiEndpoint, "src/Api/ChainsEndpoints.cs", 33);
        var testCaller = Node("t2", "ChainServiceTests", CodeNodeType.Class, "tests/Application/ChainServiceTests.cs", 7, fileRole: IndexedFileRole.Test);

        graph.GetContextForEditingAsync("t1", Arg.Any<CancellationToken>())
             .Returns(new EditingContext(target, [duplicateFileCaller, routeCaller, classCaller, testCaller, methodCaller], [], []));

        var result = await sut.GetContextForEditingAsync("t1");

        result.Should().Contain("### Direct method callers (1)");
        result.Should().Contain("ModerationWorkflow.Handle");
        result.Should().Contain("### Class/interface callers (1)");
        result.Should().Contain("`ModerationWorkflow`");
        result.Should().Contain("### Test callers (1)");
        result.Should().Contain("ChainServiceTests");
        result.Should().Contain("### Context-only file callers (1)");
        result.Should().Contain("POST /chains/{id}/moderate");
        result.Should().Contain("heuristic route metadata caller");
        result.Should().Contain("Suppressed 1 duplicate file-only callers");
        result.Should().NotContain("`ModerationWorkflow.cs` — `src/Application/ModerationWorkflow.cs`:1");
    }

    [Fact]
    public async Task GetContextForEditingAsync_WithFullDetail_IncludesRawCallerInventory()
    {
        var (sut, graph) = Build();
        var target = Node("t1", "ChainService", CodeNodeType.Class, "src/Application/ChainService.cs", 12);
        var methodCaller = Node("m1", "ModerationWorkflow.Handle", CodeNodeType.Method, "src/Application/ModerationWorkflow.cs", 28);
        var duplicateFileCaller = Node("f1", "ModerationWorkflow.cs", CodeNodeType.File, "src/Application/ModerationWorkflow.cs", 1);

        graph.GetContextForEditingAsync("t1", Arg.Any<CancellationToken>())
             .Returns(new EditingContext(target, [duplicateFileCaller, methodCaller], [], []));

        var result = await sut.GetContextForEditingAsync("t1", ContextDetailLevel.Full);

        result.Should().Contain("### Raw caller inventory (2)");
        result.Should().Contain("`ModerationWorkflow.cs`");
        result.Should().Contain("`ModerationWorkflow.Handle`");
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
        result.Should().Contain("Production candidates are prioritized by default");
        result.Should().Contain("### Production candidates (2)");
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

        result.Should().Contain("RealService");
        result.Should().NotContain("Build");
    }

    [Fact]
    public async Task FindHighChurnAsync_WhenBroaderOutputEnabled_ShowsHeuristicAndSuppressedSections()
    {
        var (sut, graph) = Build(new CodebaseAnalysisOptions
        {
            Ranking = new RankingOptions
            {
                ProductionOnlyByDefault = false,
                IncludeSuppressedNoise = true
            }
        });
        graph.FindHighChurnAsync(null, 3, Arg.Any<CancellationToken>())
            .Returns([
                (Node("prod-1", "RealService", CodeNodeType.Class, "src/RealService.cs", fileRole: IndexedFileRole.Source), 6),
                (Node("heur-1", "Orders", CodeNodeType.Namespace, "src/Orders", fileRole: IndexedFileRole.Source), 7),
                (Node("noise-1", "Build", CodeNodeType.Method, "tests/ServiceTests.cs", fileRole: IndexedFileRole.Test), 20)
            ]);

        var result = await sut.FindHighChurnAsync();

        result.Should().Contain("### Broader heuristic matches (1)");
        result.Should().Contain("### Suppressed noise (1)");
        result.Should().Contain("Orders");
        result.Should().Contain("Build");
    }

    // ── GetPageRankAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task AnalyzeChangedSubgraphAsync_WithMixedRuntimeChanges_SummarizesRiskTestsArchitectureAndDocs()
    {
        var graph = Substitute.For<ICodeGraphRepository>();
        var vector = Substitute.For<IVectorRepository>();
        var sut = new CodebaseQueryService(graph, vector);
        var backendNode = Node(
            "method:invite",
            "InviteService.AcceptAsync",
            CodeNodeType.Method,
            "src/Application/Invites/InviteService.cs",
            line: 42,
            project: "Shop",
            fileRole: IndexedFileRole.Source,
            updatedAt: DateTimeOffset.UtcNow);
        var frontendNode = Node(
            "class:panel",
            "InvitePanel",
            CodeNodeType.Class,
            "src/Web/components/invite-panel.tsx",
            line: 8,
            project: "Shop",
            fileRole: IndexedFileRole.Source,
            updatedAt: DateTimeOffset.UtcNow);
        var impactedNode = Node(
            "api:invite",
            "POST /api/invites/{code}/accept",
            CodeNodeType.ApiEndpoint,
            "src/Api/InviteEndpoints.cs",
            project: "Shop",
            fileRole: IndexedFileRole.Source);
        var testNode = Node(
            "test:invite",
            "InviteServiceTests.AcceptAsync_returns_invite_details",
            CodeNodeType.Method,
            "tests/Shop.Application.Tests/Invites/InviteServiceTests.cs",
            line: 17,
            project: "Shop",
            fileRole: IndexedFileRole.Test);
        var architectureTarget = Node(
            "infra:repo",
            "InviteRepository",
            CodeNodeType.Class,
            "src/Infrastructure/Invites/InviteRepository.cs",
            line: 12,
            project: "Shop",
            fileRole: IndexedFileRole.Source);

        graph.QueryNodesAsync(
                Arg.Is<CodeGraphQuery>(query => query.FilePathFilter == "src/Application/Invites/InviteService.cs" && query.ProjectContext == "Shop"),
                Arg.Any<CancellationToken>())
            .Returns([backendNode]);
        graph.QueryNodesAsync(
                Arg.Is<CodeGraphQuery>(query => query.FilePathFilter == "src/Web/components/invite-panel.tsx" && query.ProjectContext == "Shop"),
                Arg.Any<CancellationToken>())
            .Returns([frontendNode]);
        graph.FindImpactAsync(backendNode.Id, 2, Arg.Any<CancellationToken>())
            .Returns([(impactedNode, 1)]);
        graph.FindImpactAsync(frontendNode.Id, 2, Arg.Any<CancellationToken>())
            .Returns([]);
        graph.FindRelatedTestsAsync(backendNode.Id, "Shop", Arg.Any<CancellationToken>())
            .Returns([(testNode, "direct")]);
        graph.FindRelatedTestsAsync(frontendNode.Id, "Shop", Arg.Any<CancellationToken>())
            .Returns([]);
        graph.FindHotspotsAsync("Shop", 40, Arg.Any<CancellationToken>())
            .Returns([(backendNode, 9)]);
        graph.FindHighChurnAsync("Shop", 3, Arg.Any<CancellationToken>())
            .Returns([(backendNode, 4), (frontendNode, 5)]);
        graph.FindArchitectureViolationsAsync("Shop", Arg.Any<CancellationToken>())
            .Returns([(backendNode, architectureTarget, "Application -> Infrastructure")]);
        graph.FindSmellPathsAsync("Shop", 4, Arg.Any<CancellationToken>())
            .Returns([
                new DependencySmellPath(
                    "Application -> Infrastructure",
                    backendNode,
                    architectureTarget,
                    1,
                    [
                        new GraphPathStep(backendNode, "Calls", 1.0),
                        new GraphPathStep(architectureTarget, null, null)
                    ])
            ]);
        vector.SearchByTextAsync(Arg.Any<string>(), "Shop", 8, Arg.Any<CancellationToken>())
            .Returns([
                new KnowledgeDocument
                {
                    Id = "doc:invite-feature",
                    Source = "docs/features/invites.md",
                    ProjectContext = "Shop",
                    Content = "Invite acceptance flow."
                }
            ]);

        var result = await sut.AnalyzeChangedSubgraphAsync(
            ["src/Application/Invites/InviteService.cs", "src/Web/components/invite-panel.tsx"],
            projectContext: "Shop",
            impactDepth: 2,
            limit: 8);

        result.Should().Contain("## Changed Subgraph Analysis - Shop");
        result.Should().Contain("**Changed runtimes:** C#, TypeScript/JS");
        result.Should().Contain("**Overall risk:** high");
        result.Should().Contain("InviteService.AcceptAsync");
        result.Should().Contain("InvitePanel");
        result.Should().Contain("no related tests found");
        result.Should().Contain("Architecture violations touching the changed slice");
        result.Should().Contain("Dependency smell paths touching the changed slice");
        result.Should().Contain("docs/features/invites.md");
        result.Should().Contain("dotnet test --filter FullyQualifiedName~");
    }

    [Fact]
    public async Task AnalyzeChangedSubgraphAsync_WithDocsAndTestsOnly_SuppressesStructuralNoise()
    {
        var graph = Substitute.For<ICodeGraphRepository>();
        var vector = Substitute.For<IVectorRepository>();
        var sut = new CodebaseQueryService(graph, vector);
        var docFile = Node(
            "file:doc",
            "55-add-changed-subgraph-analysis.md",
            CodeNodeType.File,
            "docs/features/55-add-changed-subgraph-analysis.md",
            line: 1,
            project: "CodeMeridian",
            fileRole: IndexedFileRole.Unknown);
        var testMethod = Node(
            "test:changed-subgraph",
            "AnalyzeChangedSubgraphAsync_WithMixedRuntimeChanges_SummarizesRiskTestsArchitectureAndDocs",
            CodeNodeType.Method,
            "tests/CodeMeridian.Application.Tests/Services/CodebaseQueryServiceAnalyticsTests.cs",
            line: 1400,
            project: "CodeMeridian",
            fileRole: IndexedFileRole.Test);

        graph.QueryNodesAsync(
                Arg.Is<CodeGraphQuery>(query => query.FilePathFilter == "docs/features/55-add-changed-subgraph-analysis.md"),
                Arg.Any<CancellationToken>())
            .Returns([docFile]);
        graph.QueryNodesAsync(
                Arg.Is<CodeGraphQuery>(query => query.FilePathFilter == "tests/CodeMeridian.Application.Tests/Services/CodebaseQueryServiceAnalyticsTests.cs"),
                Arg.Any<CancellationToken>())
            .Returns([testMethod]);
        vector.SearchByTextAsync(Arg.Any<string>(), "CodeMeridian", 4, Arg.Any<CancellationToken>())
            .Returns([
                new KnowledgeDocument
                {
                    Id = "doc:features-index",
                    Source = "docs/features.md",
                    ProjectContext = "CodeMeridian",
                    Content = "Feature reference."
                }
            ]);

        var result = await sut.AnalyzeChangedSubgraphAsync(
            [
                "docs/features/55-add-changed-subgraph-analysis.md",
                "tests/CodeMeridian.Application.Tests/Services/CodebaseQueryServiceAnalyticsTests.cs"
            ],
            projectContext: "CodeMeridian",
            limit: 4);

        result.Should().Contain("**Overall risk:** low");
        result.Should().Contain("Production-relevant changed nodes: 0");
        result.Should().Contain("Only docs/test/configuration-style nodes were matched");
        result.Should().Contain("docs/features.md");
        await graph.DidNotReceive().FindArchitectureViolationsAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await graph.DidNotReceive().FindSmellPathsAsync(Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

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
        result.Should().Contain("### Production candidates (2)");
        result.Should().Contain("| 1 |");
        result.Should().Contain("CoreHub");
        result.Should().Contain("0.8700");
        result.Should().Contain("BaseRepo");
        result.Should().Contain("transitive call-graph influence");
    }

    [Fact]
    public async Task GetPageRankAsync_DefaultNoiseReduction_HidesBroaderAndSuppressedResults()
    {
        var (sut, graph) = Build();
        graph.GetPageRankAsync(null, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([
                (Node("prod-1", "CoreHub", CodeNodeType.Class, "src/CoreHub.cs", fileRole: IndexedFileRole.Source), 0.87),
                (Node("heur-1", "POST /orders", CodeNodeType.ApiEndpoint, "src/OrdersEndpoints.cs", fileRole: IndexedFileRole.Source), 0.91),
                (Node("noise-1", "Orders:Timeout", CodeNodeType.ConfigurationKey, "appsettings.json", fileRole: IndexedFileRole.Configuration), 0.99)
            ]);

        var result = await sut.GetPageRankAsync();

        result.Should().Contain("### Production candidates (1)");
        result.Should().Contain("CoreHub");
        result.Should().Contain("Hidden by default: 1 broader heuristic match, 1 suppressed noise node.");
        result.Should().NotContain("### Broader heuristic matches");
        result.Should().NotContain("### Suppressed noise");
        result.Should().NotContain("Orders:Timeout");
    }

    [Fact]
    public async Task GetPageRankAsync_WhenBroaderOutputEnabled_ShowsHeuristicAndSuppressedSections()
    {
        var (sut, graph) = Build(new CodebaseAnalysisOptions
        {
            Ranking = new RankingOptions
            {
                ProductionOnlyByDefault = false,
                IncludeSuppressedNoise = true
            }
        });
        graph.GetPageRankAsync(null, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([
                (Node("prod-1", "CoreHub", CodeNodeType.Class, "src/CoreHub.cs", fileRole: IndexedFileRole.Source), 0.87),
                (Node("heur-1", "POST /orders", CodeNodeType.ApiEndpoint, "src/OrdersEndpoints.cs", fileRole: IndexedFileRole.Source), 0.91),
                (Node("noise-1", "Orders:Timeout", CodeNodeType.ConfigurationKey, "appsettings.json", fileRole: IndexedFileRole.Configuration), 0.99)
            ]);

        var result = await sut.GetPageRankAsync();

        result.Should().Contain("### Broader heuristic matches (1)");
        result.Should().Contain("### Suppressed noise (1)");
        result.Should().Contain("POST /orders");
        result.Should().Contain("Orders:Timeout");
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
        graph.GetPageRankAsync(null, Arg.Any<int>(), Arg.Any<CancellationToken>())
             .Returns([]);
        graph.GetArticulationPointsAsync(null, Arg.Any<int>(), Arg.Any<CancellationToken>())
             .Returns([]);
        graph.GetBridgeEdgesAsync(null, Arg.Any<int>(), Arg.Any<CancellationToken>())
             .Returns([]);

        var result = await sut.FindBridgesAsync();

        result.Should().Contain("Risky core analysis failed");
        result.Should().Contain("Graph Data Science");
    }

    [Fact]
    public async Task FindBridgesAsync_WithResults_ReturnsRiskyCoreSignalsAndBridgeEdges()
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
        graph.GetPageRankAsync(null, Arg.Any<int>(), Arg.Any<CancellationToken>())
             .Returns([(bridge, 0.88)]);
        graph.GetArticulationPointsAsync(null, Arg.Any<int>(), Arg.Any<CancellationToken>())
             .Returns([(bridge, 3)]);
        graph.GetBridgeEdgesAsync(null, Arg.Any<int>(), Arg.Any<CancellationToken>())
             .Returns([
                 (apiCaller, bridge, (IReadOnlyList<long>)new long[] { 5, 4 }),
                 (bridge, infraCallee, (IReadOnlyList<long>)new long[] { 6, 3 })
             ]);
        graph.GetContextForEditingAsync("b1", Arg.Any<CancellationToken>())
             .Returns(new EditingContext(bridge, [apiCaller], [infraCallee], []));

        var result = await sut.FindBridgesAsync();

        result.Should().Contain("## Risky Core Nodes");
        result.Should().Contain("PaymentFacade");
        result.Should().Contain("API -> Application -> Infrastructure");
        result.Should().Contain("splits graph into 3 component(s)");
        result.Should().Contain("touches 2 bridge edge(s)");
        result.Should().Contain("find_impact");
        result.Should().Contain("### Bridge edges");
    }

    [Fact]
    public async Task FindBridgesAsync_ExcludesTestsAndDocumentationFromRankedCandidates()
    {
        var (sut, graph) = Build();
        var bridge = Node(
            "prod:bridge",
            "PaymentFacade",
            CodeNodeType.Class,
            "src/Application/Payments/PaymentFacade.cs",
            line: 12,
            project: "CodeMeridian",
            updatedAt: DateTimeOffset.UtcNow,
            sourceHash: "prod-hash",
            @namespace: "CodeMeridian.Application.Payments");
        var infra = Node(
            "prod:infra",
            "StripeGateway",
            CodeNodeType.Class,
            "src/Infrastructure/Payments/StripeGateway.cs",
            line: 20,
            project: "CodeMeridian",
            updatedAt: DateTimeOffset.UtcNow,
            sourceHash: "infra-hash",
            @namespace: "CodeMeridian.Infrastructure.Payments");
        var testHelper = Node(
            "test:build",
            "Build()",
            CodeNodeType.Method,
            "tests/CodeMeridian.Application.Tests/Services/PaymentFacadeTests.cs",
            line: 8,
            project: "CodeMeridian",
            updatedAt: DateTimeOffset.UtcNow,
            sourceHash: "test-hash",
            fileRole: IndexedFileRole.Test,
            @namespace: "CodeMeridian.Application.Tests.Services");
        var docAsset = Node(
            "doc:file",
            "docs-index.css",
            CodeNodeType.File,
            "docs/assets/docs-index.css",
            line: 1,
            project: "CodeMeridian",
            updatedAt: DateTimeOffset.UtcNow,
            sourceHash: "doc-hash");

        graph.GetBetweennessAsync(null, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([
                (bridge, 900.0),
                (testHelper, 950.0),
                (docAsset, 920.0)
            ]);
        graph.GetPageRankAsync(null, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([
                (bridge, 0.82),
                (testHelper, 0.91),
                (docAsset, 0.88)
            ]);
        graph.GetArticulationPointsAsync(null, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([
                (bridge, 4),
                (testHelper, 5),
                (docAsset, 3)
            ]);
        graph.GetBridgeEdgesAsync(null, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([
                (testHelper, bridge, (IReadOnlyList<long>)new long[] { 9, 4 }),
                (docAsset, bridge, (IReadOnlyList<long>)new long[] { 8, 3 }),
                (bridge, infra, (IReadOnlyList<long>)new long[] { 6, 2 })
            ]);
        graph.GetContextForEditingAsync(bridge.Id, Arg.Any<CancellationToken>())
            .Returns(new EditingContext(bridge, [], [infra], []));
        graph.GetContextForEditingAsync(infra.Id, Arg.Any<CancellationToken>())
            .Returns(new EditingContext(infra, [bridge], [], []));

        var result = await sut.FindBridgesAsync();

        result.Should().Contain("PaymentFacade");
        result.Should().NotContain("Build()");
        result.Should().NotContain("docs-index.css");
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
        result.Should().Contain("`Shop.Application.Payments`");
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

    [Fact]
    public async Task FindHybridSearchAsync_WithMissingFilePath_FormatsAsciiFallbackOutput()
    {
        var (sut, graph, embeddings) = BuildWithEmbeddings();
        embeddings.IsAvailableAsync(Arg.Any<CancellationToken>()).Returns(true);
        embeddings.GenerateEmbeddingAsync("retry policy", Arg.Any<CancellationToken>())
            .Returns([0.1f, 0.2f, 0.3f]);
        graph.FindHybridMatchesAsync(
                Arg.Any<float[]>(),
                null,
                3,
                "Shop",
                true,
                10,
                Arg.Any<CancellationToken>())
            .Returns([
                (Node("n1", "RetryPolicy", CodeNodeType.Class, null, null, "Shop"), 0.82)
            ]);

        var result = await sut.FindHybridSearchAsync("retry policy", projectContext: "Shop");

        result.Should().Contain("## Hybrid Semantic Graph Search - `retry policy`");
        result.Should().Contain("| 82.0% | Class | `RetryPolicy` | - |");
        result.Should().NotContain("Ã¢");
    }

    // ── FindDuplicateCandidatesAsync ─────────────────────────────────────────

    [Fact]
    public async Task FindHybridSearchAsync_WithAnchorAndNoNearbyMatches_ReturnsGuidanceInsteadOfEmbeddingFailure()
    {
        var (sut, graph, embeddings) = BuildWithEmbeddings();
        embeddings.IsAvailableAsync(Arg.Any<CancellationToken>()).Returns(true);
        embeddings.GenerateEmbeddingAsync("tool dependency impact", Arg.Any<CancellationToken>())
            .Returns([0.1f, 0.2f, 0.3f]);
        graph.FindHybridMatchesAsync(
                Arg.Any<float[]>(),
                "CodeMeridian::Method::CodeMeridian.Application.Services.CodebaseQueryService::FindStaleKnowledgeAsync(string?,int,CancellationToken)",
                3,
                "CodeMeridian",
                true,
                5,
                Arg.Any<CancellationToken>())
            .Returns([]);

        var result = await sut.FindHybridSearchAsync(
            "tool dependency impact",
            nearNodeId: "CodeMeridian::Method::CodeMeridian.Application.Services.CodebaseQueryService::FindStaleKnowledgeAsync(string?,int,CancellationToken)",
            projectContext: "CodeMeridian",
            limit: 5);

        result.Should().Be("No hybrid-search results found. Try broadening the graph neighborhood, lowering filters, or indexing more embedded nodes.");
        result.Should().NotContain("requires embeddings to be enabled");
    }

    [Fact]
    public async Task FindImplementationPatternsAsync_WhenNoPatterns_ReturnsGuidance()
    {
        var (sut, graph) = Build();
        graph.QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var result = await sut.FindImplementationPatternsAsync("invite acceptance flow", projectContext: "Shop");

        result.Should().Contain("No implementation patterns found");
        result.Should().Contain("invite acceptance flow");
    }

    [Fact]
    public async Task FindImplementationPatternsAsync_WithStructuralMatches_ReturnsRankedPatterns()
    {
        var (sut, graph, embeddings) = BuildWithEmbeddings();
        var expectedEmbedding = new[] { 0.6f, 0.2f, 0.1f, 0.1f };
        var endpoint = Node(
            "endpoint-1",
            "POST /api/invites/{id}/accept",
            CodeNodeType.ApiEndpoint,
            "src/Api/Invites/AcceptInviteEndpoint.cs",
            12,
            "Shop",
            updatedAt: DateTimeOffset.UtcNow,
            fileRole: IndexedFileRole.Source,
            @namespace: "Shop.Api.Invites");
        var service = Node(
            "service-1",
            "AcceptInviteService",
            CodeNodeType.Class,
            "src/Application/Invites/AcceptInviteService.cs",
            18,
            "Shop",
            updatedAt: DateTimeOffset.UtcNow,
            fileRole: IndexedFileRole.Source,
            @namespace: "Shop.Application.Invites");
        var contract = Node(
            "contract-1",
            "IAcceptInviteRepository",
            CodeNodeType.Interface,
            "src/Application/Invites/IAcceptInviteRepository.cs",
            4,
            "Shop",
            updatedAt: DateTimeOffset.UtcNow,
            fileRole: IndexedFileRole.Source,
            @namespace: "Shop.Application.Invites");
        var repository = Node(
            "repository-1",
            "InviteRepository",
            CodeNodeType.Class,
            "src/Infrastructure/Invites/InviteRepository.cs",
            20,
            "Shop",
            updatedAt: DateTimeOffset.UtcNow,
            fileRole: IndexedFileRole.Source,
            @namespace: "Shop.Infrastructure.Invites");
        var directTest = Node(
            "test-1",
            "AcceptInviteServiceTests",
            CodeNodeType.Class,
            "tests/Application/Invites/AcceptInviteServiceTests.cs",
            9,
            "Shop",
            updatedAt: DateTimeOffset.UtcNow,
            fileRole: IndexedFileRole.Test,
            @namespace: "Shop.Tests.Invites");
        var boundary = new CodeNode
        {
            Id = "boundary-1",
            Name = "InviteAcceptanceWrite",
            Type = CodeNodeType.ExternalConcept,
            FilePath = "src/Infrastructure/Invites/InviteRepository.cs",
            LineNumber = 38,
            ProjectContext = "Shop",
            UpdatedAt = DateTimeOffset.UtcNow,
            FileRole = IndexedFileRole.Source,
            Properties = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["externalKind"] = "DatabaseOperation"
            }
        };

        embeddings.IsAvailableAsync(Arg.Any<CancellationToken>()).Returns(true);
        embeddings.GenerateEmbeddingAsync("invite acceptance flow", Arg.Any<CancellationToken>())
            .Returns(expectedEmbedding);
        graph.QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>())
            .Returns([endpoint]);
        graph.FindImplementationPatternCandidatesAsync(
                Arg.Is<float[]>(embedding => embedding.SequenceEqual(expectedEmbedding)),
                "Shop",
                true,
                24,
                Arg.Any<CancellationToken>())
            .Returns([(endpoint, 0.93)]);
        graph.GetContextForEditingAsync(endpoint.Id, Arg.Any<CancellationToken>())
            .Returns(new EditingContext(endpoint, [], [service], [contract]));
        graph.FindImpactAsync(endpoint.Id, 2, Arg.Any<CancellationToken>())
            .Returns([]);
        graph.FindDownstreamAsync(endpoint.Id, 3, Arg.Any<CancellationToken>())
            .Returns([(repository, 1), (boundary, 2)]);
        graph.FindRelatedTestsAsync(endpoint.Id, "Shop", Arg.Any<CancellationToken>())
            .Returns([(directTest, "direct")]);

        var result = await sut.FindImplementationPatternsAsync("invite acceptance flow", projectContext: "Shop");

        result.Should().Contain("## Structural Implementation Patterns");
        result.Should().Contain("embedding and lexical graph seeds with structural reranking");
        result.Should().Contain("api/command entry -> application/domain -> contract -> repository/store -> database/event boundary -> tests");
        result.Should().Contain("POST /api/invites/{id}/accept");
        result.Should().Contain("AcceptInviteService");
        result.Should().Contain("InviteRepository");
        result.Should().Contain("AcceptInviteServiceTests");
        result.Should().Contain("### Pattern details");
        await graph.Received(1).FindImplementationPatternCandidatesAsync(Arg.Any<float[]>(), "Shop", true, 24, Arg.Any<CancellationToken>());
    }

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
        result.Should().Contain("ExternalConcept");
    }

    [Fact]
    public async Task FindDuplicateCandidatesAsync_WithFrontendStyleDeclarations_ReturnsNearDuplicateClusters()
    {
        var (sut, graph) = Build();
        graph.QueryNodesAsync(Arg.Is<CodeGraphQuery>(q => q.TypeFilter == CodeNodeType.ExternalConcept), Arg.Any<CancellationToken>())
            .Returns([
                CreateFrontendStyleDeclaration("n1", ".card", "src/web/Card.scss", 10, "padding", "1rem"),
                CreateFrontendStyleDeclaration("n2", ".card--wide", "src/web/Card.scss", 16, "padding", "16px"),
                CreateFrontendStyleDeclaration("n3", ".card--hero", "src/web/HeroCard.scss", 4, "padding", "1.02rem"),
                CreateFrontendStyleDeclaration("n4", ".panel", "src/web/Panel.scss", 9, "color", "#ff0000"),
                CreateFrontendStyleDeclaration("n5", ".panel--muted", "src/web/Panel.scss", 14, "color", "rgb(250, 4, 4)")
            ]);

        var result = await sut.FindDuplicateCandidatesAsync(
            projectContext: "Shop.Web",
            nodeType: "ExternalConcept");

        result.Should().Contain("## Frontend Style Near-Duplicate Clusters - Shop.Web");
        result.Should().Contain("`padding`");
        result.Should().Contain("bounded numeric/unit drift");
        result.Should().Contain("16px");
        result.Should().Contain("1.02rem");
        result.Should().Contain("base-class variant review around `.card`");
        result.Should().Contain("colors within Euclidean RGBA distance");
        result.Should().Contain("`color`");
        result.Should().Contain(".panel--muted");
    }

    [Fact]
    public async Task FindFrontendCascadeConflictsAsync_WithIndexedSpecificityMetadata_ReturnsLikelyOverrides()
    {
        var (sut, graph) = Build();
        graph.QueryNodesAsync(Arg.Is<CodeGraphQuery>(q => q.TypeFilter == CodeNodeType.ExternalConcept), Arg.Any<CancellationToken>())
            .Returns([
                CreateFrontendStyleDeclaration("d1", ".hero", "src/web/site.scss", 4, "color", "red", new Dictionary<string, string>
                {
                    ["sourceOrder"] = "1",
                    ["specificityA"] = "0",
                    ["specificityB"] = "1",
                    ["specificityC"] = "0",
                    ["targetClassConceptsCsv"] = "hero"
                }),
                CreateFrontendStyleDeclaration("d2", ".hero", "src/web/site.scss", 8, "color", "blue", new Dictionary<string, string>
                {
                    ["sourceOrder"] = "2",
                    ["specificityA"] = "0",
                    ["specificityB"] = "1",
                    ["specificityC"] = "0",
                    ["targetClassConceptsCsv"] = "hero"
                }),
                CreateFrontendStyleDeclaration("d3", ".layout .hero", "src/web/site.scss", 12, "color", "navy", new Dictionary<string, string>
                {
                    ["sourceOrder"] = "3",
                    ["specificityA"] = "0",
                    ["specificityB"] = "2",
                    ["specificityC"] = "0",
                    ["targetClassConceptsCsv"] = "layout,hero"
                }),
                CreateFrontendStyleDeclaration("d4", "#page .hero", "src/web/site.scss", 16, "color", "white", new Dictionary<string, string>
                {
                    ["sourceOrder"] = "4",
                    ["specificityA"] = "1",
                    ["specificityB"] = "1",
                    ["specificityC"] = "0",
                    ["targetClassConceptsCsv"] = "hero",
                    ["targetIdConceptsCsv"] = "page"
                })
            ]);

        var result = await sut.FindFrontendCascadeConflictsAsync(projectContext: "Shop.Web");

        result.Should().Contain("## Frontend Cascade Conflicts - Shop.Web");
        result.Should().Contain("likely override/conflict relationships");
        result.Should().Contain("same specificity `0,1,0` and later source order");
        result.Should().Contain("higher specificity `0,2,0` over `0,1,0`");
        result.Should().Contain("`CssClass:hero`");
        result.Should().Contain("Suspiciously Specific Selectors");
        result.Should().Contain(".layout .hero");
        result.Should().Contain("#page .hero");
        result.Should().Contain("inferred from indexed selector specificity");
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
        var (sut, graph) = Build();
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
        var (sut, graph) = Build();
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
        result.Should().Contain("Direct regression tests:");
        result.Should().Contain("Suggested command: `dotnet test --filter FullyQualifiedName~OrderServiceTests`");
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
        result.Should().NotContain("â");
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
                Node("m1", "FindStaleKnowledgeAsync", CodeNodeType.Method, "TODO.md", 1, "CodeMeridian", updatedAt: DateTimeOffset.UtcNow, sourceHash: "todo"),
                Node("c1", "CodebaseQueryService", CodeNodeType.Class, "src/Application/Services/CodebaseQueryService.Analytics.cs", 1, "CodeMeridian", updatedAt: DateTimeOffset.UtcNow, sourceHash: "code")
            ]);

        var result = await sut.FindImplementationSurfaceAsync(
            "add stale knowledge query",
            "stale,knowledge",
            "CodeMeridian");

        result.Should().Contain("## Implementation Surface");
        result.Should().Contain("### Primary Edit Targets");
        result.Should().Contain("### Context-Only Targets");
        result.Should().Contain("TODO.md");
        result.Should().Contain("FindStaleKnowledgeAsync");
        result.Should().Contain("Target confidence");
        result.Should().Contain("file-only");
        result.Should().Contain("documentation file is context, not the edit surface");
        result.Should().Contain("Freshness");
        result.IndexOf("src/Application/Services/CodebaseQueryService.Analytics.cs", StringComparison.Ordinal)
            .Should()
            .BeLessThan(result.IndexOf("TODO.md", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FindImplementationSurfaceAsync_WithFrontendConceptMatch_ExpandsToConnectedMarkupAndStylesheetFiles()
    {
        var (sut, graph) = Build();
        var heroClass = new CodeNode
        {
            Id = "Shop.Web:ExternalConcept:CssClass:hero",
            Name = "hero",
            Type = CodeNodeType.ExternalConcept,
            ProjectContext = "Shop.Web",
            Properties = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["externalKind"] = "CssClass"
            }
        };
        var markupFile = Node("markup", "HeroCard.tsx", CodeNodeType.File, "src/web/HeroCard.tsx", 1, "Shop.Web");
        var stylesheetFile = Node("style", "HeroCard.scss", CodeNodeType.File, "src/web/HeroCard.scss", 1, "Shop.Web");
        var selectorNode = new CodeNode
        {
            Id = "Shop.Web:ExternalConcept:CssSelector:hero",
            Name = ".hero",
            Type = CodeNodeType.ExternalConcept,
            ProjectContext = "Shop.Web",
            Properties = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["externalKind"] = "CssSelector"
            }
        };

        graph
            .QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>())
            .Returns([heroClass]);
        graph
            .FindImpactAsync(heroClass.Id, 2, Arg.Any<CancellationToken>())
            .Returns([(markupFile, 1), (selectorNode, 1)]);
        graph
            .FindDownstreamAsync(heroClass.Id, 2, Arg.Any<CancellationToken>())
            .Returns([(selectorNode, 1), (stylesheetFile, 2)]);

        var result = await sut.FindImplementationSurfaceAsync(
            "update hero class styling",
            "hero,scss",
            "Shop.Web");

        result.Should().Contain("src/web/HeroCard.tsx");
        result.Should().Contain("src/web/HeroCard.scss");
        result.Should().Contain("frontend graph matches");
        result.Should().Contain("Target confidence");
    }

    [Fact]
    public async Task FindImplementationSurfaceAsync_UsesPrecisionFeedbackToExplainAcceptedAndIgnoredTargets()
    {
        var feedbackPath = WritePrecisionFeedbackFile(
            """
            {
              "project": "CodeMeridian",
              "sessionFile": ".meridian/sessions/session.jsonl",
              "generatedAtUtc": "2026-06-19T00:00:00Z",
              "tools": [
                {
                  "toolName": "mcp__CodeMeridian.find_implementation_surface",
                  "suggestedFileCount": 2,
                  "acceptedFileCount": 1,
                  "ignoredFileCount": 1,
                  "suggestedTestCount": 0,
                  "acceptedTestCount": 0,
                  "ignoredTestCount": 0,
                  "exactTargets": 1,
                  "fileOnlyTargets": 1,
                  "heuristicTargets": 0,
                  "staleTargets": 0,
                  "staleWarnings": 0,
                  "manualFallbackCommands": 0,
                  "files": [
                    { "path": "src/Application/Preferred.cs", "suggestedCount": 1, "acceptedCount": 1, "ignoredCount": 0 },
                    { "path": "src/Application/Broad.cs", "suggestedCount": 1, "acceptedCount": 0, "ignoredCount": 1 }
                  ],
                  "tests": []
                }
              ]
            }
            """);
        var (sut, graph) = Build(new CodebaseAnalysisOptions
        {
            PrecisionFeedback = new PrecisionFeedbackOptions
            {
                FeedbackFilePath = feedbackPath
            }
        });
        graph
            .QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>())
            .Returns([
                Node("preferred", "PreferredTarget", CodeNodeType.Class, "src/Application/Preferred.cs", 1, "CodeMeridian", updatedAt: DateTimeOffset.UtcNow, sourceHash: "abc"),
                Node("broad", "BroadTarget", CodeNodeType.Class, "src/Application/Broad.cs", 1, "CodeMeridian", updatedAt: DateTimeOffset.UtcNow, sourceHash: "def")
            ]);

        var result = await sut.FindImplementationSurfaceAsync("preferred target", projectContext: "CodeMeridian");

        result.IndexOf("src/Application/Preferred.cs", StringComparison.Ordinal)
            .Should()
            .BeLessThan(result.IndexOf("src/Application/Broad.cs", StringComparison.Ordinal));
        result.Should().Contain("feedback accepted 1/1 prior sessions");
        result.Should().Contain("feedback ignored 1/1 prior sessions");
    }

    [Fact]
    public async Task FindImplementationSurfaceAsync_PrimaryTargetCanDrivePlanEditRouteAnchor()
    {
        var (sut, graph) = Build();
        var service = Node("s1", "PaymentService", CodeNodeType.Class, "src/Application/Payments/PaymentService.cs", 12, "Shop", updatedAt: DateTimeOffset.UtcNow, sourceHash: "svc");
        var implementation = Node("r1", "SqlPaymentRepository", CodeNodeType.Class, "src/Infrastructure/Payments/SqlPaymentRepository.cs", 9, "Shop");
        var endpoint = Node("e1", "PaymentEndpoint", CodeNodeType.Method, "src/McpServer/Api/PaymentEndpoint.cs", 20, "Shop");
        var test = Node("t1", "PaymentServiceTests", CodeNodeType.Class, "tests/Shop.Tests/PaymentServiceTests.cs", 8, "Shop");

        graph
            .QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>())
            .Returns([service]);
        graph
            .GetContextForEditingAsync(service.Id, Arg.Any<CancellationToken>())
            .Returns(new EditingContext(service, [endpoint], [implementation], []));
        graph
            .FindImpactAsync(service.Id, 2, Arg.Any<CancellationToken>())
            .Returns([(endpoint, 1)]);
        graph
            .FindDownstreamAsync(service.Id, 2, Arg.Any<CancellationToken>())
            .Returns([(implementation, 1)]);
        graph
            .FindRelatedTestsAsync(service.Id, "Shop", Arg.Any<CancellationToken>())
            .Returns([(test, "direct")]);

        var surface = await sut.FindImplementationSurfaceAsync(
            "replace repository pattern in payments",
            "repository,payments",
            "Shop");
        var route = await sut.PlanEditRouteAsync(
            "replace repository pattern in payments",
            "repository,payments",
            "Shop");

        surface.Should().Contain("`src/Application/Payments/PaymentService.cs`");
        route.Should().Contain("**Anchor:** `PaymentService` (Class) - `src/Application/Payments/PaymentService.cs`");
        route.Should().Contain("Route confidence:** High");
        route.Should().Contain("Run `build_minimal_context` on exact route targets before changing code.");
    }

    [Fact]
    public async Task FindImplementationSurfaceAsync_PrunesTestsGeneratedAndBroadFileOnlyTargetsIntoContextOnly()
    {
        var (sut, graph) = Build();
        graph
            .QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>())
            .Returns([
                Node("impl", "ChargeAsync", CodeNodeType.Method, "src/Payments/PaymentGateway.cs", 42, "Shop", updatedAt: DateTimeOffset.UtcNow, sourceHash: "abc", fileRole: IndexedFileRole.Source),
                Node("test", "PaymentGatewayTests", CodeNodeType.Class, "tests/Payments/PaymentGatewayTests.cs", 5, "Shop", updatedAt: DateTimeOffset.UtcNow, sourceHash: "def", fileRole: IndexedFileRole.Test),
                Node("gen", "PaymentGateway.Generated", CodeNodeType.Class, "src/Generated/PaymentGateway.g.cs", 1, "Shop", updatedAt: DateTimeOffset.UtcNow, sourceHash: "ghi", fileRole: IndexedFileRole.Generated),
                Node("fileOnly", "PaymentGateway.cs", CodeNodeType.File, "src/Payments/PaymentGateway.csproj.user", 1, "Shop", updatedAt: DateTimeOffset.UtcNow, sourceHash: "jkl")
            ]);

        var result = await sut.FindImplementationSurfaceAsync(
            "update payment gateway charge flow",
            "payment,gateway,charge",
            "Shop");

        result.Should().Contain("### Primary Edit Targets");
        result.Should().Contain("`src/Payments/PaymentGateway.cs`");
        result.Should().Contain("### Context-Only Targets");
        result.Should().Contain("tests/Payments/PaymentGatewayTests.cs");
        result.Should().Contain("test target is verification context");
        result.Should().Contain("src/Generated/PaymentGateway.g.cs");
        result.Should().Contain("generated file should not be the primary edit surface");
        result.Should().Contain("src/Payments/PaymentGateway.csproj.user");
        result.Should().Contain("broad file match without an edit-ready symbol anchor");
    }

    [Fact]
    public async Task FindImplementationSurfaceAsync_WhenOnlyContextCandidatesExist_PromotesBestAvailableTarget()
    {
        var (sut, graph) = Build();
        graph
            .QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>())
            .Returns([
                Node("doc", "Architecture Note", CodeNodeType.File, "docs/architecture.md", 1, "CodeMeridian", updatedAt: DateTimeOffset.UtcNow),
                Node("todo", "TODO", CodeNodeType.File, "TODO.md", 1, "CodeMeridian", updatedAt: DateTimeOffset.UtcNow)
            ]);

        var result = await sut.FindImplementationSurfaceAsync(
            "architecture note update",
            projectContext: "CodeMeridian");

        result.Should().Contain("### Primary Edit Targets");
        result.Should().Contain("docs/architecture.md");
        result.Should().Contain("### Context-Only Targets");
        result.Should().Contain("TODO.md");
    }

    [Fact]
    public async Task FindImplementationSurfaceAsync_PrefersGoalTermMatchesOverBroadRepositoryInterfaces()
    {
        var (sut, graph) = Build();
        graph
            .QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>())
            .Returns([
                Node("repo", "ICodeGraphRepository", CodeNodeType.Interface, "src/Core/CodeGraph/ICodeGraphRepository.cs", 1, "CodeMeridian", updatedAt: DateTimeOffset.UtcNow, sourceHash: "repo"),
                Node("stale", "FindStaleKnowledgeAsync", CodeNodeType.Method, "src/Application/Services/CodebaseQueryService.Analytics.cs", 42, "CodeMeridian", updatedAt: DateTimeOffset.UtcNow, sourceHash: "stale", summary: "Detects stale knowledge after renames and reindexing.")
            ]);

        var result = await sut.FindImplementationSurfaceAsync(
            "add stale knowledge query",
            projectContext: "CodeMeridian");

        result.IndexOf("src/Application/Services/CodebaseQueryService.Analytics.cs", StringComparison.Ordinal)
            .Should()
            .BeLessThan(result.IndexOf("src/Core/CodeGraph/ICodeGraphRepository.cs", StringComparison.Ordinal));
        result.Should().Contain("goal-term matches");
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
        result.Should().Contain("Confidence:**");
        result.Should().Contain("FeatureImplementationAnalysisService");
        result.Should().Contain("Presentation/MCP");
        result.Should().Contain("Focused verification plan:");
        result.Should().Contain("Direct regression tests:");
        result.Should().Contain("FeatureImplementationAnalysisServiceTests");
        result.Should().Contain("docs/features/39-add-feature-implementation-path.md");
        result.Should().Contain("Risk level");
    }

    [Fact]
    public async Task AnalyzeFeatureImplementationPathAsync_CategorizesFeatureTestsAndSuggestsCommand()
    {
        var graph = Substitute.For<ICodeGraphRepository>();
        var vector = Substitute.For<IVectorRepository>();
        var sut = new CodebaseQueryService(graph, vector);
        var service = Node(
            "s1",
            "KeywordGraphJobService",
            CodeNodeType.Class,
            "src/Application/Services/KeywordGraphJobService.cs",
            12,
            "CodeMeridian",
            updatedAt: DateTimeOffset.UtcNow,
            lineCount: 60,
            sourceHash: "abc",
            summary: "Runs keyword graph rebuild jobs.");
        var tool = Node(
            "m1",
            "KeywordsStatusEndpoint",
            CodeNodeType.ApiEndpoint,
            "src/McpServer/Api/KeywordApiEndpoints.cs",
            40,
            "CodeMeridian",
            updatedAt: DateTimeOffset.UtcNow,
            lineCount: 20,
            sourceHash: "def",
            summary: "Returns keyword status details.");
        var directTest = Node(
            "t1",
            "KeywordGraphJobServiceTests",
            CodeNodeType.Class,
            "tests/Application/KeywordGraphJobServiceTests.cs",
            5,
            "CodeMeridian",
            fileRole: IndexedFileRole.Test);
        var apiTest = Node(
            "t2",
            "KeywordApiEndpointTests",
            CodeNodeType.Class,
            "tests/Api/KeywordApiEndpointTests.cs",
            8,
            "CodeMeridian",
            fileRole: IndexedFileRole.Test);

        vector
            .SearchByTextAsync("keyword graph jobs", "CodeMeridian", 6, Arg.Any<CancellationToken>())
            .Returns([]);
        graph
            .QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>())
            .Returns([service, tool]);
        graph
            .FindRelatedTestsAsync(service.Id, "CodeMeridian", Arg.Any<CancellationToken>())
            .Returns([(directTest, "direct")]);
        graph
            .FindRelatedTestsAsync(tool.Id, "CodeMeridian", Arg.Any<CancellationToken>())
            .Returns([(apiTest, "direct")]);

        var result = await sut.AnalyzeFeatureImplementationPathAsync(
            "keyword graph jobs",
            "CodeMeridian");

        result.Should().Contain("Focused verification plan:");
        result.Should().Contain("Direct regression tests:");
        result.Should().Contain("KeywordGraphJobServiceTests");
        result.Should().Contain("Contract/API forwarding tests:");
        result.Should().Contain("KeywordApiEndpointTests");
        result.Should().Contain("Suggested command:");
    }

    [Fact]
    public async Task AnalyzeFeatureImplementationPathAsync_UsesPrecisionFeedbackInSurfaceReasons()
    {
        var feedbackPath = WritePrecisionFeedbackFile(
            """
            {
              "project": "CodeMeridian",
              "sessionFile": ".meridian/sessions/session.jsonl",
              "generatedAtUtc": "2026-06-19T00:00:00Z",
              "tools": [
                {
                  "toolName": "mcp__CodeMeridian.analyze_feature_implementation_path",
                  "suggestedFileCount": 1,
                  "acceptedFileCount": 1,
                  "ignoredFileCount": 0,
                  "suggestedTestCount": 0,
                  "acceptedTestCount": 0,
                  "ignoredTestCount": 0,
                  "exactTargets": 1,
                  "fileOnlyTargets": 0,
                  "heuristicTargets": 0,
                  "staleTargets": 0,
                  "staleWarnings": 0,
                  "manualFallbackCommands": 0,
                  "files": [
                    { "path": "src/Application/Services/FeatureImplementationAnalysisService.cs", "suggestedCount": 1, "acceptedCount": 1, "ignoredCount": 0 }
                  ],
                  "tests": []
                }
              ]
            }
            """);
        var graph = Substitute.For<ICodeGraphRepository>();
        var vector = Substitute.For<IVectorRepository>();
        var sut = new CodebaseQueryService(
            graph,
            vector,
            Options.Create(new CodebaseAnalysisOptions
            {
                PrecisionFeedback = new PrecisionFeedbackOptions
                {
                    FeedbackFilePath = feedbackPath
                }
            }));
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
            summary: "Maps feature docs to implementation surfaces.");

        vector
            .SearchByTextAsync("Add Feature Implementation Path", "CodeMeridian", 6, Arg.Any<CancellationToken>())
            .Returns([]);
        graph
            .QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>())
            .Returns([service]);
        graph
            .FindRelatedTestsAsync(service.Id, "CodeMeridian", Arg.Any<CancellationToken>())
            .Returns([]);

        var result = await sut.AnalyzeFeatureImplementationPathAsync(
            "Add Feature Implementation Path",
            "CodeMeridian");

        result.Should().Contain("feedback accepted 1/1 prior sessions");
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
    public async Task AnalyzeFeatureImplementationPathAsync_PrefersFeatureDocTermsOverGenericDocumentWords()
    {
        var graph = Substitute.For<ICodeGraphRepository>();
        var vector = Substitute.For<IVectorRepository>();
        var sut = new CodebaseQueryService(graph, vector);
        var featureNode = Node(
            "surface",
            "GetContextForEditingAsync",
            CodeNodeType.Method,
            "src/Application/Services/CodebaseQueryService.Surface.cs",
            12,
            "CodeMeridian",
            updatedAt: DateTimeOffset.UtcNow,
            sourceHash: "surface",
            summary: "Builds derived edit surface context for refactor workflows.");
        var noisyNode = Node(
            "repo",
            "ICodeGraphRepository",
            CodeNodeType.Interface,
            "src/Core/CodeGraph/ICodeGraphRepository.cs",
            5,
            "CodeMeridian",
            updatedAt: DateTimeOffset.UtcNow,
            sourceHash: "repo",
            summary: "Repository contract for graph queries.");

        vector
            .SearchByTextAsync("docs/features/56-add-derived-edit-surface-credit-for-extraction-refactors.md", "CodeMeridian", 6, Arg.Any<CancellationToken>())
            .Returns([
                new KnowledgeDocument
                {
                    Id = "feature-56",
                    Source = "docs/features/56-add-derived-edit-surface-credit-for-extraction-refactors.md",
                    ProjectContext = "CodeMeridian",
                    Content = """
                              # Add Derived Edit Surface Credit For Extraction Refactors
                              - Status: pending
                              Suggested files from a prior session should receive derived edit-surface credit.
                              """
                }
            ]);
        graph
            .QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>())
            .Returns([noisyNode, featureNode]);
        graph
            .FindRelatedTestsAsync(Arg.Any<string>(), "CodeMeridian", Arg.Any<CancellationToken>())
            .Returns([]);

        var result = await sut.AnalyzeFeatureImplementationPathAsync(
            "docs/features/56-add-derived-edit-surface-credit-for-extraction-refactors.md",
            "CodeMeridian");

        result.IndexOf("src/Application/Services/CodebaseQueryService.Surface.cs", StringComparison.Ordinal)
            .Should()
            .BeLessThan(result.IndexOf("src/Core/CodeGraph/ICodeGraphRepository.cs", StringComparison.Ordinal));
        result.Should().Contain("`derived`");
        result.Should().Contain("`surface`");
        result.Should().NotContain("`suggested`");
        result.Should().NotContain("`files`");
        result.Should().NotContain("`session`");
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
    public async Task PlanEditRouteAsync_PrefersProductionAnchorOverDocsAndTests()
    {
        var (sut, graph) = Build();
        var doc = Node("d1", "Add Payments Feature", CodeNodeType.File, "docs/features/add-payments.md", 1, "Shop");
        var test = Node("t1", "PaymentServiceTests", CodeNodeType.Class, "tests/Shop.Tests/PaymentServiceTests.cs", 8, "Shop", fileRole: IndexedFileRole.Test);
        var port = Node("i1", "IPaymentRepository", CodeNodeType.Interface, "src/Application/Ports/IPaymentRepository.cs", 1, "Shop");
        var service = Node("s1", "PaymentService", CodeNodeType.Class, "src/Application/Payments/PaymentService.cs", 12, "Shop", fileRole: IndexedFileRole.Source);
        var implementation = Node("r1", "SqlPaymentRepository", CodeNodeType.Class, "src/Infrastructure/Payments/SqlPaymentRepository.cs", 9, "Shop", fileRole: IndexedFileRole.Source);
        var endpoint = Node("e1", "PaymentEndpoint", CodeNodeType.Method, "src/McpServer/Api/PaymentEndpoint.cs", 20, "Shop", fileRole: IndexedFileRole.Source);

        graph
            .QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>())
            .Returns([doc, test, service, port, implementation, endpoint]);
        graph
            .GetContextForEditingAsync(service.Id, Arg.Any<CancellationToken>())
            .Returns(new EditingContext(service, [endpoint], [implementation], [port]));
        graph
            .FindImpactAsync(service.Id, 2, Arg.Any<CancellationToken>())
            .Returns([(endpoint, 1)]);
        graph
            .FindDownstreamAsync(service.Id, 2, Arg.Any<CancellationToken>())
            .Returns([(implementation, 1)]);
        graph
            .FindRelatedTestsAsync(service.Id, "Shop", Arg.Any<CancellationToken>())
            .Returns([(test, "direct")]);

        var result = await sut.PlanEditRouteAsync(
            "replace repository pattern in payments",
            "repository,payments",
            "Shop");

        result.Should().Contain("**Anchor:** `PaymentService` (Class) - `src/Application/Payments/PaymentService.cs`");
        result.Should().Contain("Implementation candidates: 4");
        result.Should().Contain("PaymentServiceTests");
        result.Should().NotContain("Add Payments Feature");
        result.Should().NotContain("docs/features/add-payments.md");
    }

    [Fact]
    public async Task PlanEditRouteAsync_PrefersExactGoalTargetOutsideApplicationAndDomain()
    {
        var (sut, graph) = Build();
        var target = Node("cfg1", "CodeMeridianConfigFileStore", CodeNodeType.Class, "src/Tooling/Configuration/CodeMeridianConfigFileStore.cs", 7, "CodeMeridian", fileRole: IndexedFileRole.Source);
        var helper = Node("core1", "FindRelatedTestsAsync(string,string?,CancellationToken)", CodeNodeType.Method, "src/Core/CodeGraph/ICodeGraphRepository.cs", 49, "CodeMeridian", fileRole: IndexedFileRole.Source);
        var writeMethod = Node("cfg-write", "Write(DirectoryInfo,string?,string,bool,bool)", CodeNodeType.Method, "src/Tooling/Configuration/CodeMeridianConfigFileStore.cs", 118, "CodeMeridian", fileRole: IndexedFileRole.Source);

        graph
            .QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>())
            .Returns([helper, target, writeMethod]);
        graph
            .GetContextForEditingAsync(target.Id, Arg.Any<CancellationToken>())
            .Returns(new EditingContext(target, [], [writeMethod], []));
        graph
            .FindImpactAsync(target.Id, 2, Arg.Any<CancellationToken>())
            .Returns([]);
        graph
            .FindDownstreamAsync(target.Id, 2, Arg.Any<CancellationToken>())
            .Returns([(writeMethod, 1)]);
        graph
            .FindRelatedTestsAsync(target.Id, "CodeMeridian", Arg.Any<CancellationToken>())
            .Returns([]);

        var result = await sut.PlanEditRouteAsync(
            "refactor CodeMeridianConfigFileStore into smaller collaborators for template IO and write behavior",
            "configuration,templates,file-io",
            "CodeMeridian");

        result.Should().Contain("**Anchor:** `CodeMeridianConfigFileStore` (Class) - `src/Tooling/Configuration/CodeMeridianConfigFileStore.cs`");
        result.Should().NotContain("**Anchor:** `FindRelatedTestsAsync");
    }

    [Fact]
    public async Task PlanEditRouteAsync_PrunesUnrelatedSemanticMatchesFromStructuredRouteStages()
    {
        var (sut, graph) = Build();
        var target = Node("cfg1", "CodeMeridianConfigFileStore", CodeNodeType.Class, "src/Tooling/Configuration/CodeMeridianConfigFileStore.cs", 7, "CodeMeridian", fileRole: IndexedFileRole.Source);
        var unrelated = Node("dup1", "DuplicateCandidate", CodeNodeType.Class, "src/Core/CodeGraph/DuplicateCandidate.cs", 6, "CodeMeridian", fileRole: IndexedFileRole.Source, @namespace: "CodeMeridian.Core.CodeGraph");
        var writeMethod = Node("cfg-write", "Write(DirectoryInfo,string?,string,bool,bool)", CodeNodeType.Method, "src/Tooling/Configuration/CodeMeridianConfigFileStore.cs", 118, "CodeMeridian", fileRole: IndexedFileRole.Source);
        var configTests = Node("t1", "IndexerConfigTests", CodeNodeType.Class, "tests/CodeMeridian.Indexer.Tests/Cli/IndexerConfigTests.cs", 6, "CodeMeridian", fileRole: IndexedFileRole.Test);

        graph
            .QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>())
            .Returns([target, unrelated, writeMethod]);
        graph
            .GetContextForEditingAsync(target.Id, Arg.Any<CancellationToken>())
            .Returns(new EditingContext(target, [], [writeMethod], []));
        graph
            .FindImpactAsync(target.Id, 2, Arg.Any<CancellationToken>())
            .Returns([]);
        graph
            .FindDownstreamAsync(target.Id, 2, Arg.Any<CancellationToken>())
            .Returns([(writeMethod, 1)]);
        graph
            .FindRelatedTestsAsync(target.Id, "CodeMeridian", Arg.Any<CancellationToken>())
            .Returns([(configTests, "direct")]);

        var result = await sut.PlanEditRouteAsync(
            "refactor CodeMeridianConfigFileStore safely",
            "configuration,templates,file-io",
            "CodeMeridian");

        result.Should().Contain("**Anchor:** `CodeMeridianConfigFileStore` (Class) - `src/Tooling/Configuration/CodeMeridianConfigFileStore.cs`");
        result.Should().Contain("IndexerConfigTests");
        result.Should().NotContain("DuplicateCandidate");
        result.Should().NotContain("src/Core/CodeGraph/DuplicateCandidate.cs");
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
    public async Task ResolveExactSymbolAsync_WithClassTarget_PrefersExactClassBeforeConstructors()
    {
        var (sut, graph) = Build();
        var classNode = Node(
            "CodeMeridian::Class::CodeMeridian.Application.Services.CodebaseQueryService",
            "CodebaseQueryService",
            CodeNodeType.Class,
            "src/Application/Services/CodebaseQueryService.ToolDependencyImpact.cs",
            5,
            "CodeMeridian",
            updatedAt: DateTimeOffset.UtcNow,
            lineCount: 160,
            sourceHash: "class-hash");
        var constructorOne = Node(
            "CodeMeridian::Method::CodeMeridian.Application.Services.CodebaseQueryService::CodebaseQueryService(ICodeGraphRepository,IVectorRepository)",
            "CodebaseQueryService(ICodeGraphRepository,IVectorRepository)",
            CodeNodeType.Method,
            "src/Application/Services/CodebaseQueryService.cs",
            22,
            "CodeMeridian",
            updatedAt: DateTimeOffset.UtcNow,
            lineCount: 12,
            sourceHash: "ctor-1");
        var constructorTwo = Node(
            "CodeMeridian::Method::CodeMeridian.Application.Services.CodebaseQueryService::CodebaseQueryService(ICodeGraphRepository,IVectorRepository,IEmbeddingProvider)",
            "CodebaseQueryService(ICodeGraphRepository,IVectorRepository,IEmbeddingProvider)",
            CodeNodeType.Method,
            "src/Application/Services/CodebaseQueryService.cs",
            49,
            "CodeMeridian",
            updatedAt: DateTimeOffset.UtcNow,
            lineCount: 12,
            sourceHash: "ctor-2");

        graph
            .QueryNodesAsync(
                Arg.Is<CodeGraphQuery>(q =>
                    q.NameFilter == "CodebaseQueryService"
                    && q.ProjectContext == "CodeMeridian"),
                Arg.Any<CancellationToken>())
            .Returns([constructorOne, constructorTwo, classNode]);

        var result = await sut.ResolveExactSymbolAsync(
            "CodebaseQueryService",
            projectContext: "CodeMeridian");

        result.Should().Contain("## Exact Symbol Resolution");
        result.Should().Contain(classNode.Id);
        result.IndexOf(classNode.Id, StringComparison.Ordinal)
            .Should()
            .BeLessThan(result.IndexOf(constructorOne.Id, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ResolveExactSymbolAsync_WhenBroadLookupMissesExactClass_UsesTypedFallbackQueries()
    {
        var (sut, graph) = Build();
        var classNode = Node(
            "CodeMeridian::Class::CodeMeridian.Application.Services.CodebaseQueryService",
            "CodebaseQueryService",
            CodeNodeType.Class,
            "src/Application/Services/CodebaseQueryService.Surface.cs",
            6,
            "CodeMeridian",
            updatedAt: DateTimeOffset.UtcNow,
            lineCount: 964,
            sourceHash: "class-hash");
        var constructorOne = Node(
            "CodeMeridian::Method::CodeMeridian.Application.Services.CodebaseQueryService::CodebaseQueryService(ICodeGraphRepository,IVectorRepository)",
            "CodebaseQueryService(ICodeGraphRepository,IVectorRepository)",
            CodeNodeType.Method,
            "src/Application/Services/CodebaseQueryService.cs",
            22,
            "CodeMeridian",
            updatedAt: DateTimeOffset.UtcNow,
            lineCount: 12,
            sourceHash: "ctor-1");
        var constructorTwo = Node(
            "CodeMeridian::Method::CodeMeridian.Application.Services.CodebaseQueryService::CodebaseQueryService(ICodeGraphRepository,IVectorRepository,IEmbeddingProvider)",
            "CodebaseQueryService(ICodeGraphRepository,IVectorRepository,IEmbeddingProvider)",
            CodeNodeType.Method,
            "src/Application/Services/CodebaseQueryService.cs",
            49,
            "CodeMeridian",
            updatedAt: DateTimeOffset.UtcNow,
            lineCount: 12,
            sourceHash: "ctor-2");

        graph.QueryNodesAsync(
                Arg.Is<CodeGraphQuery>(q =>
                    q.NameFilter == "CodebaseQueryService"
                    && q.ProjectContext == "CodeMeridian"
                    && q.TypeFilter == null),
                Arg.Any<CancellationToken>())
            .Returns([constructorOne, constructorTwo]);
        graph.QueryNodesAsync(
                Arg.Is<CodeGraphQuery>(q =>
                    q.NameFilter == "CodebaseQueryService"
                    && q.ProjectContext == "CodeMeridian"
                    && q.TypeFilter == CodeNodeType.Class),
                Arg.Any<CancellationToken>())
            .Returns([classNode]);
        graph.QueryNodesAsync(
                Arg.Is<CodeGraphQuery>(q =>
                    q.NameFilter == "CodebaseQueryService"
                    && q.ProjectContext == "CodeMeridian"
                    && q.TypeFilter == CodeNodeType.Interface),
                Arg.Any<CancellationToken>())
            .Returns([]);
        graph.QueryNodesAsync(
                Arg.Is<CodeGraphQuery>(q =>
                    q.NameFilter == "CodebaseQueryService"
                    && q.ProjectContext == "CodeMeridian"
                    && q.TypeFilter == CodeNodeType.Method),
                Arg.Any<CancellationToken>())
            .Returns([constructorOne, constructorTwo]);
        graph.QueryNodesAsync(
                Arg.Is<CodeGraphQuery>(q =>
                    q.NameFilter == "CodebaseQueryService"
                    && q.ProjectContext == "CodeMeridian"
                    && q.TypeFilter == CodeNodeType.File),
                Arg.Any<CancellationToken>())
            .Returns([]);

        var result = await sut.ResolveExactSymbolAsync(
            "CodebaseQueryService",
            projectContext: "CodeMeridian");

        result.Should().Contain("## Exact Symbol Resolution");
        result.Should().Contain("**Confidence summary:** 1 exact");
        result.Should().Contain(classNode.Id);
        result.IndexOf(classNode.Id, StringComparison.Ordinal)
            .Should()
            .BeLessThan(result.IndexOf(constructorOne.Id, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ResolveExactSymbolAsync_WithDuplicateMethodNamesAndFileHint_PrefersMatchingCanonicalNode()
    {
        var (sut, graph) = Build();
        var applicationNode = Node(
            "CodeMeridian::Method::CodeMeridian.Application.Services.CodebaseQueryService::BuildMinimalContextAsync(string,string?,int,bool,bool,bool,bool,ContextDetailLevel,CancellationToken)",
            "BuildMinimalContextAsync",
            CodeNodeType.Method,
            "src/Application/Services/CodebaseQueryService.Analytics.cs",
            938,
            "CodeMeridian",
            updatedAt: DateTimeOffset.UtcNow,
            lineCount: 140,
            sourceHash: "app-build-minimal-context");
        var mcpNode = Node(
            "CodeMeridian::Method::CodeMeridian.McpServer.Tools.CodebaseTools::BuildMinimalContextAsync(string,string?,int,bool,bool,bool,bool,ContextDetailLevel,CancellationToken)",
            "BuildMinimalContextAsync",
            CodeNodeType.Method,
            "src/McpServer/Tools/CodebaseTools.Analytics.cs",
            174,
            "CodeMeridian",
            updatedAt: DateTimeOffset.UtcNow,
            lineCount: 20,
            sourceHash: "mcp-build-minimal-context");

        graph
            .QueryNodesAsync(
                Arg.Is<CodeGraphQuery>(q =>
                    q.NameFilter == "BuildMinimalContextAsync"
                    && q.FilePathFilter == "src/Application/Services/CodebaseQueryService.Analytics.cs"
                    && q.ProjectContext == "CodeMeridian"),
                Arg.Any<CancellationToken>())
            .Returns([applicationNode, mcpNode]);

        var result = await sut.ResolveExactSymbolAsync(
            "BuildMinimalContextAsync",
            "src/Application/Services/CodebaseQueryService.Analytics.cs",
            line: 940,
            projectContext: "CodeMeridian");

        result.Should().Contain("## Exact Symbol Resolution");
        result.Should().Contain("**Confidence summary:** 1 exact");
        result.Should().Contain(applicationNode.Id);
        result.Should().NotContain(mcpNode.Id);
        result.Should().Contain("file match");
        result.Should().Contain("near line hint");
    }

    [Fact]
    public async Task GetContextForEditingAsync_WithNamespaceStyleId_ResolvesCanonicalNodeId()
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

        graph.QueryNodesAsync(
                Arg.Is<CodeGraphQuery>(q =>
                    q.NameFilter == "CodeMeridianConfigFileStore"
                    && q.ProjectContext == null),
                Arg.Any<CancellationToken>())
            .Returns([target]);
        graph.GetContextForEditingAsync(target.Id, Arg.Any<CancellationToken>())
            .Returns(new EditingContext(target, [], [], []));

        var result = await sut.GetContextForEditingAsync("CodeMeridian.Tooling.Configuration.CodeMeridianConfigFileStore");

        result.Should().Contain("## Edit Context");
        result.Should().Contain("CodeMeridianConfigFileStore");
        await graph.Received(1).GetContextForEditingAsync(target.Id, Arg.Any<CancellationToken>());
        await graph.DidNotReceive().GetContextForEditingAsync("CodeMeridian.Tooling.Configuration.CodeMeridianConfigFileStore", Arg.Any<CancellationToken>());
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

    [Fact]
    public async Task FindImplementationSurfaceAsync_FileOnlyTarget_PairsWithResolveExactSymbolGuidance()
    {
        var (sut, graph) = Build();
        var fileOnly = Node(
            "File:CodeMeridian.Application.Services.CodebaseQueryService.Surface.cs",
            "CodebaseQueryService.Surface.cs",
            CodeNodeType.File,
            "src/Application/Services/CodebaseQueryService.Surface.cs",
            1,
            "CodeMeridian",
            updatedAt: DateTimeOffset.UtcNow,
            sourceHash: "surface-file");
        var exact = Node(
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
                Arg.Any<CodeGraphQuery>(),
                Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var query = ci.Arg<CodeGraphQuery>();
                if (query.NameFilter == "FindImplementationSurfaceAsync")
                    return [exact];

                return [fileOnly];
            });

        var surface = await sut.FindImplementationSurfaceAsync(
            "find implementation surface ranking",
            projectContext: "CodeMeridian");
        var resolved = await sut.ResolveExactSymbolAsync(
            "FindImplementationSurfaceAsync",
            "src/Application/Services/CodebaseQueryService.Surface.cs",
            line: 10,
            projectContext: "CodeMeridian");

        surface.Should().Contain("Target confidence");
        surface.Should().Contain("file-only");
        surface.Should().Contain("Use `resolve_exact_symbol` when target confidence is not exact.");
        resolved.Should().Contain("**Confidence summary:** 1 exact");
        resolved.Should().Contain("FindImplementationSurfaceAsync");
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
    public async Task CheckGraphFreshnessAsync_TreatsConfigurationNodesAsExpectedMetadataShapes()
    {
        var (sut, graph) = Build();
        graph
            .QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>())
            .Returns([
                Node("cfg-key", "Embedding:Enabled", CodeNodeType.ConfigurationKey, project: "CodeMeridian", updatedAt: DateTimeOffset.UtcNow),
                Node("cfg-entry", "Embedding__Enabled", CodeNodeType.ConfigurationEntry, ".env", project: "CodeMeridian", updatedAt: DateTimeOffset.UtcNow),
                Node("cfg-file", ".env", CodeNodeType.ConfigurationFile, ".env", project: "CodeMeridian", updatedAt: DateTimeOffset.UtcNow, sourceHash: "env-hash")
            ]);

        var result = await sut.CheckGraphFreshnessAsync(projectContext: "CodeMeridian");

        result.Should().Contain("## Graph Freshness");
        result.Should().Contain("3 High, 0 Medium, 0 Low confidence");
        result.Should().Contain("not required");
        result.Should().Contain("structural node with content-update metadata");
        result.Should().Contain("indexer supplied the metadata expected for this node type");
    }

    [Fact]
    public async Task CheckGraphFreshnessAsync_WhenProjectContextHasNoNodes_SuggestsClosestProject()
    {
        var (sut, graph) = Build();
        graph
            .QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>())
            .Returns([]);
        graph
            .GetProjectContextsAsync("code3meridian", Arg.Any<CancellationToken>())
            .Returns(["CodeMeridian"]);

        var result = await sut.CheckGraphFreshnessAsync(projectContext: "code3meridian");

        result.Should().Contain("No graph nodes found in 'code3meridian'");
        result.Should().Contain("Did you mean 'CodeMeridian'?");
    }

    [Fact]
    public async Task CheckGraphFreshnessAsync_WithCanonicalizableProjectContext_UsesCanonicalProject()
    {
        var (sut, graph) = Build();
        var target = Node(
            "fresh",
            "Fresh",
            CodeNodeType.Class,
            "src/Fresh.cs",
            1,
            "CodeMeridian",
            updatedAt: DateTimeOffset.UtcNow,
            lineCount: 12,
            sourceHash: "abc");

        graph.GetProjectContextsAsync("code-meridian", Arg.Any<CancellationToken>())
            .Returns(["CodeMeridian"]);
        graph.QueryNodesAsync(
                Arg.Is<CodeGraphQuery>(q => q.ProjectContext == "CodeMeridian"),
                Arg.Any<CancellationToken>())
            .Returns([target]);

        var result = await sut.CheckGraphFreshnessAsync(projectContext: "code-meridian");

        result.Should().Contain("## Graph Freshness - CodeMeridian");
        result.Should().Contain("Fresh");
        result.Should().NotContain("No graph nodes found");
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

    [Fact]
    public async Task FindGraphDriftAsync_IgnoresStructuralAndConfigurationMetadataThatIsNotRequired()
    {
        var (sut, graph) = Build();
        graph
            .QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>())
            .Returns([
                Node("cfg-key", "Embedding:Enabled", CodeNodeType.ConfigurationKey, project: "CodeMeridian", updatedAt: DateTimeOffset.UtcNow),
                Node("cfg-entry", "Embedding__Enabled", CodeNodeType.ConfigurationEntry, ".env", project: "CodeMeridian", updatedAt: DateTimeOffset.UtcNow),
                Node("api", "POST /nodes", CodeNodeType.ApiEndpoint, project: "CodeMeridian", updatedAt: DateTimeOffset.UtcNow),
                Node("cfg-file", ".env", CodeNodeType.ConfigurationFile, ".env", project: "CodeMeridian", updatedAt: DateTimeOffset.UtcNow, sourceHash: "env-hash")
            ]);

        var result = await sut.FindGraphDriftAsync("CodeMeridian");

        result.Should().Be("Graph drift: low for 'CodeMeridian'. Indexed file metadata, line metadata, source hashes, and update timestamps look consistent. Source files are not read by the MCP server.");
    }
}
