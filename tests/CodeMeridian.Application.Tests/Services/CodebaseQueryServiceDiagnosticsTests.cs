using CodeMeridian.Application.Services;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;
using FluentAssertions;
using NSubstitute;

namespace CodeMeridian.Application.Tests.Services;

public sealed class CodebaseQueryServiceDiagnosticsTests
{
    [Fact]
    public async Task FindDiagnosticsForNodeAsync_WhenFileHasNoDiagnostics_DoesNotSuggestGlobalSetup()
    {
        var graph = Substitute.For<ICodeGraphRepository>();
        var vectors = Substitute.For<IVectorRepository>();
        graph.FindDiagnosticsForNodeAsync("method:Save", Arg.Any<CancellationToken>()).Returns([]);
        var sut = new CodebaseQueryService(graph, vectors);

        var result = await sut.FindDiagnosticsForNodeAsync("method:Save");

        result.Should().Contain("No diagnostics found in this node's file");
        result.Should().NotContain("Run the indexer");
    }
}
