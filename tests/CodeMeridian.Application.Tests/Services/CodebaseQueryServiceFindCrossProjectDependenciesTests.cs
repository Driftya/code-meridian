using CodeMeridian.Application.Services;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace CodeMeridian.Application.Tests.Services;

public sealed class CodebaseQueryServiceFindCrossProjectDependenciesTests : CodebaseQueryServiceAnalyticsTestBase
{
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


}

