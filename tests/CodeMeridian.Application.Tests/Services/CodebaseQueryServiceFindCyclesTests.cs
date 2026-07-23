using CodeMeridian.Application.Services;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace CodeMeridian.Application.Tests.Services;

public sealed class CodebaseQueryServiceFindCyclesTests : CodebaseQueryServiceAnalyticsTestBase
{
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
        result.Should().Contain("| Namespace A |");
        result.Should().Contain("↔");
        result.Should().NotContain("A?B");
        result.Should().Contain("abstraction");
    }

    // ── FindArchitectureViolationsAsync ───────────────────────────────────────


}
