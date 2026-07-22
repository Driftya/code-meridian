using CodeMeridian.Application.Services;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace CodeMeridian.Application.Tests.Services;

public sealed class CodebaseQueryServiceFindConnectionTests : CodebaseQueryServiceAnalyticsTestBase
{
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


}

