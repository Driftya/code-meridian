using CodeMeridian.Application.Services;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;
using FluentAssertions;
using NSubstitute;

namespace CodeMeridian.Application.Tests.Services;

public sealed class CodebaseQueryServiceStructuralQueryTests
{
    [Fact]
    public async Task QueryStructureAsync_CallersIntent_ReturnsGraphCallersForExactTarget()
    {
        var (sut, graph) = Build();
        var target = Node("Project::Method::Sample.Service::SaveAsync()", "SaveAsync()", CodeNodeType.Method, "src/Service.cs");
        var caller = Node("Project::Method::Sample.Controller::Post()", "Post()", CodeNodeType.Method, "src/Controller.cs");
        graph.QueryNodesAsync(Arg.Is<CodeGraphQuery>(query => query.NameFilter == "SaveAsync"), Arg.Any<CancellationToken>())
            .Returns([target]);
        graph.GetContextForEditingAsync(target.Id, Arg.Any<CancellationToken>())
            .Returns(new EditingContext(target, [caller], [], []));

        var result = await sut.QueryStructureAsync("callers of SaveAsync");

        result.Should().Contain("## Callers");
        result.Should().Contain(target.Id);
        result.Should().Contain(caller.Id);
    }

    [Fact]
    public async Task QueryStructureAsync_AmbiguousStructuralTarget_ReturnsCandidatesWithoutMixingResults()
    {
        var (sut, graph) = Build();
        var first = Node("A::Run()", "Run()", CodeNodeType.Method, "src/A.cs");
        var second = Node("B::Run()", "Run()", CodeNodeType.Method, "src/B.cs");
        graph.QueryNodesAsync(Arg.Is<CodeGraphQuery>(query => query.NameFilter == "Run"), Arg.Any<CancellationToken>())
            .Returns([first, second]);

        var result = await sut.QueryStructureAsync("dependencies of Run");

        result.Should().Contain("is ambiguous");
        result.Should().Contain(first.Id);
        result.Should().Contain(second.Id);
        await graph.DidNotReceive().GetSubgraphSummaryAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task QueryStructureAsync_ImplementationsIntent_ReturnsIncomingImplementationFacts()
    {
        var (sut, graph) = Build();
        var contract = Node("Project::Interface::Sample.IStore", "IStore", CodeNodeType.Interface, "src/IStore.cs");
        var implementation = Node("Project::Class::Sample.Store", "Store", CodeNodeType.Class, "src/Store.cs");
        graph.QueryNodesAsync(Arg.Is<CodeGraphQuery>(query => query.NameFilter == "IStore"), Arg.Any<CancellationToken>())
            .Returns([contract]);
        graph.GetContextForEditingAsync(contract.Id, Arg.Any<CancellationToken>())
            .Returns(new EditingContext(contract, [implementation], [], []));

        var result = await sut.QueryStructureAsync("implementations of IStore");

        result.Should().Contain("## Implementations");
        result.Should().Contain(implementation.Id);
    }

    [Fact]
    public async Task QueryStructureAsync_MembersIntent_FiltersByNamespace()
    {
        var (sut, graph) = Build();
        graph.QueryNodesAsync(Arg.Is<CodeGraphQuery>(query => query.NameFilter == null && query.Limit == 500), Arg.Any<CancellationToken>())
            .Returns([
                Node("A", "OrderService", CodeNodeType.Class, "src/OrderService.cs", "Sample.Orders"),
                Node("B", "PaymentService", CodeNodeType.Class, "src/PaymentService.cs", "Sample.Payments")
            ]);

        var result = await sut.QueryStructureAsync("types in namespace Sample.Orders");

        result.Should().Contain("OrderService");
        result.Should().NotContain("PaymentService");
    }

    [Fact]
    public async Task QueryStructureAsync_OpenEndedQuestion_PreservesSemanticFallback()
    {
        var (sut, graph) = Build();
        var node = Node("A", "RetryPolicy", CodeNodeType.Class, "src/RetryPolicy.cs");
        graph.QueryNodesAsync(Arg.Is<CodeGraphQuery>(query => query.SemanticQuery == "where is retry handled"), Arg.Any<CancellationToken>())
            .Returns([node]);
        graph.GetSubgraphSummaryAsync(node.Id, Arg.Any<CancellationToken>()).Returns("retry summary");

        var result = await sut.QueryStructureAsync("where is retry handled");

        result.Should().Contain("retry summary");
    }

    private static (CodebaseQueryService Sut, ICodeGraphRepository Graph) Build()
    {
        var graph = Substitute.For<ICodeGraphRepository>();
        var vectors = Substitute.For<IVectorRepository>();
        return (new CodebaseQueryService(graph, vectors), graph);
    }

    private static CodeNode Node(
        string id,
        string name,
        CodeNodeType type,
        string file,
        string? @namespace = "Sample") => new()
    {
        Id = id,
        Name = name,
        Type = type,
        FilePath = file,
        Namespace = @namespace,
        ProjectContext = "Project",
        UpdatedAt = DateTimeOffset.UtcNow,
        LastIndexedAt = DateTimeOffset.UtcNow,
        SourceHash = "hash",
        FileRole = IndexedFileRole.Source
    };
}
