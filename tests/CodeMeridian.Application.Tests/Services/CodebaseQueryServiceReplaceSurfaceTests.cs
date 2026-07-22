using CodeMeridian.Application.Services;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace CodeMeridian.Application.Tests.Services;

public sealed class CodebaseQueryServiceReplaceSurfaceTests : CodebaseQueryServiceAnalyticsTestBase
{
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


}

