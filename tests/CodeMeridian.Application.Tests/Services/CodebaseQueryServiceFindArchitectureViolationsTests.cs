using CodeMeridian.Application.Services;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace CodeMeridian.Application.Tests.Services;

public sealed class CodebaseQueryServiceFindArchitectureViolationsTests : CodebaseQueryServiceAnalyticsTestBase
{
    [Fact]
    public async Task FindArchitectureViolationsAsync_WhenNoViolations_ReturnsCleanMessage()
    {
        var (sut, graph) = Build();
        graph.FindArchitectureViolationsAsync(null, Arg.Any<CancellationToken>())
             .Returns([]);

        var result = await sut.FindArchitectureViolationsAsync();

        result.Should().Contain("No architecture violations found");
        result.Should().Contain("Configured architecture layers");
    }

    [Fact]
    public async Task FindArchitectureViolationsAsync_WithViolations_ReturnsTable()
    {
        var (sut, graph) = Build();
        var source = Node("s1", "CoreService", CodeNodeType.Class, "src/Core/Svc.cs");
        var target = Node("t1", "DbContext",   CodeNodeType.Class, "src/Infrastructure/Db.cs");

        graph.FindArchitectureViolationsAsync(null, Arg.Any<CancellationToken>())
             .Returns([(source, target, "Core → MyApp.Infrastructure")]);

        var result = await sut.FindArchitectureViolationsAsync();

        result.Should().Contain("## Architecture Violations");
        result.Should().Contain("configured architecture layer rules");
        result.Should().Contain("CoreService");
        result.Should().Contain("DbContext");
        result.Should().Contain("Core → MyApp.Infrastructure");
        result.Should().Contain(".meridian/architecture.json");
    }

    // ── FindHighChurnAsync ────────────────────────────────────────────────────


}

