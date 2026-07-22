using CodeMeridian.Application.Services;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace CodeMeridian.Application.Tests.Services;

public sealed class CodebaseQueryServiceGetArchitectureWeatherReportTests : CodebaseQueryServiceAnalyticsTestBase
{
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
    public async Task GetArchitectureWeatherReportAsync_WhenGraphEmpty_ReturnsIndexerGuidanceAndProjectHint()
    {
        var (sut, graph) = Build();
        graph.CountCodeNodesAsync("Shpo", Arg.Any<CancellationToken>()).Returns(0);
        graph.GetProjectContextsAsync("Shpo", Arg.Any<CancellationToken>()).Returns(["Shop"]);

        var result = await sut.GetArchitectureWeatherReportAsync("Shpo");

        result.Should().Contain("No graph nodes found in 'Shpo'.");
        result.Should().Contain("Available projects: 'Shop'.");
        result.Should().Contain("Run the indexer before generating an architecture report.");
    }

    [Fact]
    public async Task GetArchitectureWeatherReportAsync_WhenBetweennessFails_ReportsBridgeUnavailable()
    {
        var (sut, graph) = Build();
        graph.CountCodeNodesAsync("Shop", Arg.Any<CancellationToken>()).Returns(12);
        graph.CountCallEdgesAsync("Shop", Arg.Any<CancellationToken>()).Returns(24);
        graph.FindCyclesAsync("Shop", Arg.Any<CancellationToken>()).Returns([]);
        graph.FindArchitectureViolationsAsync("Shop", Arg.Any<CancellationToken>()).Returns([]);
        graph.FindCoverageGapsAsync("Shop", Arg.Any<CancellationToken>()).Returns([]);
        graph.QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>())
            .Returns([
                Node("fresh", "Fresh", CodeNodeType.Class, "src/Fresh.cs", 1, "Shop", updatedAt: DateTimeOffset.UtcNow, lineCount: 12, sourceHash: "abc")
            ]);
        graph.GetBetweennessAsync("Shop", 10, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("No such procedure: gds.betweenness.stream"));

        var result = await sut.GetArchitectureWeatherReportAsync("Shop");

        result.Should().Contain("# Architecture Weather Report - Shop");
        result.Should().Contain("Bridge nodes: unavailable (No such procedure: gds.betweenness.stream)");
    }


}

