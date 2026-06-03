using CodeMeridian.Application.Services;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;
using FluentAssertions;
using NSubstitute;

namespace CodeMeridian.Application.Tests.Services;

public sealed class CodebaseQueryServiceTests
{
    private readonly ICodeGraphRepository _codeGraph = Substitute.For<ICodeGraphRepository>();
    private readonly IVectorRepository _vectorStore = Substitute.For<IVectorRepository>();
    private readonly CodebaseQueryService _sut;

    public CodebaseQueryServiceTests() =>
        _sut = new CodebaseQueryService(_codeGraph, _vectorStore);

    [Fact]
    public async Task QueryStructureAsync_WhenNoNodes_ReturnsIngestGuidance()
    {
        _codeGraph
            .QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var result = await _sut.QueryStructureAsync("find UserService");

        result.Should().Contain("Ingest");
    }

    [Fact]
    public async Task QueryStructureAsync_WithNodes_IncludesSummaries()
    {
        var node = new CodeNode
        {
            Id = "node-1",
            Name = "UserService",
            Type = CodeNodeType.Class,
            FilePath = "src/Services/UserService.cs"
        };

        _codeGraph
            .QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>())
            .Returns([node]);

        _codeGraph
            .GetSubgraphSummaryAsync("node-1", Arg.Any<CancellationToken>())
            .Returns("**Class: UserService**\nFile: `src/Services/UserService.cs`");

        var result = await _sut.QueryStructureAsync("UserService", "MyApi");

        result.Should().Contain("UserService");
        result.Should().Contain("1");
    }

    [Fact]
    public async Task GetOverviewAsync_WhenNoData_ReturnsIngestGuidance()
    {
        _codeGraph
            .QueryNodesAsync(Arg.Any<CodeGraphQuery>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var result = await _sut.GetOverviewAsync("EmptyProject");

        result.Should().Contain("No code graph data");
    }

    [Fact]
    public async Task GetOverviewAsync_WithData_ReturnsStructuredMarkdown()
    {
        var classNode = new CodeNode { Id = "c1", Name = "OrderService", Type = CodeNodeType.Class };
        var ifaceNode = new CodeNode { Id = "i1", Name = "IOrderRepository", Type = CodeNodeType.Interface };

        _codeGraph
            .QueryNodesAsync(Arg.Is<CodeGraphQuery>(q => q.TypeFilter == CodeNodeType.Namespace), Arg.Any<CancellationToken>())
            .Returns([]);
        _codeGraph
            .QueryNodesAsync(Arg.Is<CodeGraphQuery>(q => q.TypeFilter == CodeNodeType.Class), Arg.Any<CancellationToken>())
            .Returns([classNode]);
        _codeGraph
            .QueryNodesAsync(Arg.Is<CodeGraphQuery>(q => q.TypeFilter == CodeNodeType.Interface), Arg.Any<CancellationToken>())
            .Returns([ifaceNode]);

        var result = await _sut.GetOverviewAsync("OrdersApi");

        result.Should().Contain("Classes");
        result.Should().Contain("| Classes | 1 |");  // count is shown in the summary table
        result.Should().Contain("IOrderRepository");  // interfaces are listed by name
    }

    [Fact]
    public async Task SearchDocumentationAsync_WhenNoResults_ReturnsIngestGuidance()
    {
        _vectorStore
            .SearchByTextAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var result = await _sut.SearchDocumentationAsync("authentication strategy");

        result.Should().Contain("ingest_document");
    }
}

