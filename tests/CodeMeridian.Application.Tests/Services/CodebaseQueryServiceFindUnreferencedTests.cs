using CodeMeridian.Application.Services;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace CodeMeridian.Application.Tests.Services;

public sealed class CodebaseQueryServiceFindUnreferencedTests : CodebaseQueryServiceAnalyticsTestBase
{
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
    public async Task FindUnreferencedAsync_WithResults_GroupsByTypeAndIncludesDisclaimer()
    {
        var (sut, graph) = Build();
        graph.FindUnreferencedAsync("Proj", Arg.Any<CancellationToken>())
             .Returns([Node("m1", "OldMethod", CodeNodeType.Method, "src/Old.cs", 42, "Proj"),
                       Node("c1", "DeadClass", CodeNodeType.Class, "src/Dead.cs", null, "Proj")]);

        var result = await sut.FindUnreferencedAsync("Proj");

        result.Should().Contain("## Unreferenced Code — Proj");
        result.Should().Contain("**2** methods/classes");
        result.Should().Contain("### Classes"); // grouped header
        result.Should().Contain("### Methods");
        result.Should().Contain("`OldMethod`");
        result.Should().Contain(":42");
        result.Should().Contain("entry points");
    }

    // ── FindCrossProjectDependenciesAsync ─────────────────────────────────────


}

