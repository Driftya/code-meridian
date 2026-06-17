using CodeMeridian.Application.Services;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;
using FluentAssertions;
using NSubstitute;

namespace CodeMeridian.Application.Tests.Services;

public sealed class CodebaseQueryServiceResponsibilitySliceTests
{
    [Fact]
    public async Task SuggestResponsibilitySlicesAsync_WithSharedDependenciesAndWorkflowCallers_ReturnsSlices()
    {
        var graph = Substitute.For<ICodeGraphRepository>();
        var vector = Substitute.For<IVectorRepository>();
        var sut = new CodebaseQueryService(graph, vector);
        var target = Node(
            "class:OrderService",
            "OrderService",
            CodeNodeType.Class,
            "src/Application/Services/OrderService.cs",
            1,
            "Shop",
            lineCount: 640,
            sourceHash: "class-hash",
            @namespace: "Shop.Application.Services");
        var place = Node("method:PlaceOrderAsync", "PlaceOrderAsync", CodeNodeType.Method, target.FilePath, 20, "Shop");
        var price = Node("method:CalculatePriceAsync", "CalculatePriceAsync", CodeNodeType.Method, target.FilePath, 80, "Shop");
        var cancel = Node("method:CancelOrderAsync", "CancelOrderAsync", CodeNodeType.Method, target.FilePath, 160, "Shop");
        var repository = Node("iface:IOrderRepository", "IOrderRepository", CodeNodeType.Interface, "src/Application/Orders/IOrderRepository.cs", 4, "Shop");
        var endpoint = Node("endpoint:orders", "POST /api/orders", CodeNodeType.ApiEndpoint, "src/Api/OrdersEndpoint.cs", 10, "Shop");
        var controller = Node("method:OrdersController.Post", "OrdersController.Post", CodeNodeType.Method, "src/Api/OrdersController.cs", 12, "Shop");
        var tool = Node("method:OrderTools.Place", "OrderTools.Place", CodeNodeType.Method, "src/McpServer/Tools/OrderTools.cs", 18, "Shop");
        var command = Node("method:CancelOrderCommand", "CancelOrderCommand", CodeNodeType.Method, "src/Cli/OrdersCommand.cs", 18, "Shop");
        var cancelTool = Node("method:OrderTools.Cancel", "OrderTools.Cancel", CodeNodeType.Method, "src/McpServer/Tools/OrderTools.cs", 32, "Shop");
        var placeTests = Node("test:OrderPlacementTests", "OrderPlacementTests", CodeNodeType.Class, "tests/Orders/OrderPlacementTests.cs", 5, "Shop", fileRole: IndexedFileRole.Test);

        graph.QueryNodesAsync(
                Arg.Is<CodeGraphQuery>(query => query.TypeFilter == CodeNodeType.Class && query.NameFilter == "OrderService"),
                Arg.Any<CancellationToken>())
            .Returns([target]);
        graph.QueryNodesAsync(
                Arg.Is<CodeGraphQuery>(query => query.TypeFilter == CodeNodeType.Method),
                Arg.Any<CancellationToken>())
            .Returns([place, price, cancel]);
        graph.QueryEdgesAsync(target.Id, 1, Arg.Any<CancellationToken>())
            .Returns([
                Contains(target, place),
                Contains(target, price),
                Contains(target, cancel)
            ]);
        graph.GetContextForEditingAsync(place.Id, Arg.Any<CancellationToken>())
            .Returns(new EditingContext(place, [endpoint, controller, tool], [repository], []));
        graph.GetContextForEditingAsync(price.Id, Arg.Any<CancellationToken>())
            .Returns(new EditingContext(price, [endpoint, controller], [repository], []));
        graph.GetContextForEditingAsync(cancel.Id, Arg.Any<CancellationToken>())
            .Returns(new EditingContext(cancel, [command, cancelTool], [repository], []));
        graph.FindRelatedTestsAsync(place.Id, "Shop", Arg.Any<CancellationToken>())
            .Returns([(placeTests, "direct")]);
        graph.FindRelatedTestsAsync(price.Id, "Shop", Arg.Any<CancellationToken>())
            .Returns([(placeTests, "direct")]);
        graph.FindRelatedTestsAsync(cancel.Id, "Shop", Arg.Any<CancellationToken>())
            .Returns([]);
        vector.SearchByTextAsync("OrderService", "Shop", 5, Arg.Any<CancellationToken>())
            .Returns([new KnowledgeDocument { Id = "doc1", Source = "docs/features/orders.md", Content = "Orders feature" }]);

        var result = await sut.SuggestResponsibilitySlicesAsync("OrderService", "Shop", maxSlices: 3);

        result.Should().Contain("## Responsibility Slice Suggestions - `OrderService`");
        result.Should().Contain("Recommended namespace root:** `Shop.Application.Orders`");
        result.Should().Contain("Recommended folder root:** `src/Application/Orders`");
        result.Should().Contain("`OrderService`");
        result.Should().Contain("`CancelOrderCommandService`");
        result.Should().Contain("PlaceOrderAsync");
        result.Should().Contain("CancelOrderAsync");
        result.Should().Contain("OrderPlacementTests");
        result.Should().Contain("`facade_first_extraction`");
    }

    [Fact]
    public async Task SuggestResponsibilitySlicesAsync_WithGenericOnlyMethods_ReturnsDeferExtraction()
    {
        var graph = Substitute.For<ICodeGraphRepository>();
        var vector = Substitute.For<IVectorRepository>();
        var sut = new CodebaseQueryService(graph, vector);
        var target = Node("class:LegacyService", "LegacyService", CodeNodeType.Class, "src/Application/LegacyService.cs", 1, "Shop");
        var handle = Node("method:HandleAsync", "HandleAsync", CodeNodeType.Method, target.FilePath, 20, "Shop");
        var process = Node("method:ProcessAsync", "ProcessAsync", CodeNodeType.Method, target.FilePath, 40, "Shop");

        graph.QueryNodesAsync(Arg.Is<CodeGraphQuery>(query => query.TypeFilter == CodeNodeType.Class), Arg.Any<CancellationToken>())
            .Returns([target]);
        graph.QueryNodesAsync(Arg.Is<CodeGraphQuery>(query => query.TypeFilter == CodeNodeType.Method), Arg.Any<CancellationToken>())
            .Returns([handle, process]);
        graph.QueryEdgesAsync(target.Id, 1, Arg.Any<CancellationToken>())
            .Returns([Contains(target, handle), Contains(target, process)]);
        graph.GetContextForEditingAsync(handle.Id, Arg.Any<CancellationToken>())
            .Returns(new EditingContext(handle, [], [], []));
        graph.GetContextForEditingAsync(process.Id, Arg.Any<CancellationToken>())
            .Returns(new EditingContext(process, [], [], []));
        graph.FindRelatedTestsAsync(Arg.Any<string>(), "Shop", Arg.Any<CancellationToken>())
            .Returns([]);
        vector.SearchByTextAsync("LegacyService", "Shop", 5, Arg.Any<CancellationToken>())
            .Returns([]);

        var result = await sut.SuggestResponsibilitySlicesAsync("LegacyService", "Shop");

        result.Should().Contain("`defer_extraction`");
        result.Should().Contain("did not share enough caller, dependency, test, or workflow evidence");
    }

    private static CodeNode Node(
        string id,
        string name,
        CodeNodeType type,
        string? file = null,
        int? line = null,
        string? project = null,
        int? lineCount = null,
        string? sourceHash = null,
        IndexedFileRole fileRole = IndexedFileRole.Source,
        string? @namespace = null) => new()
    {
        Id = id,
        Name = name,
        Type = type,
        FilePath = file,
        LineNumber = line,
        ProjectContext = project,
        UpdatedAt = DateTimeOffset.UtcNow,
        LineCount = lineCount,
        SourceHash = sourceHash,
        FileRole = fileRole,
        Namespace = @namespace
    };

    private static CodeEdge Contains(CodeNode source, CodeNode target) => new()
    {
        SourceId = source.Id,
        TargetId = target.Id,
        Type = CodeEdgeType.Contains
    };
}
