using CodeMeridian.Application.Services;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace CodeMeridian.Application.Tests.Services;

public sealed class CodebaseQueryServiceFindArchitectureErosionTimelineTests : CodebaseQueryServiceAnalyticsTestBase
{
    [Fact]
    public async Task FindArchitectureErosionTimelineAsync_WithSignals_ReturnsDailyTrend()
    {
        var (sut, graph) = Build();
        var oldDate = DateTimeOffset.UtcNow.AddDays(-20);
        var currentDate = DateTimeOffset.UtcNow;
        var source = Node("s1", "CoreService", CodeNodeType.Class, "src/Core/Svc.cs", updatedAt: oldDate);
        var target = Node("t1", "DbContext", CodeNodeType.Class, "src/Infrastructure/Db.cs", updatedAt: oldDate);
        var godClass = Node(
            "g1",
            "MegaService",
            CodeNodeType.Class,
            "src/MegaService.cs",
            updatedAt: currentDate,
            lineCount: 700,
            fileRole: IndexedFileRole.Source);

        graph.FindArchitectureViolationsAsync("Shop", Arg.Any<CancellationToken>())
             .Returns([(source, target, "Core -> Infrastructure")]);
        graph.FindCyclesAsync("Shop", Arg.Any<CancellationToken>())
             .Returns([("Shop.Core", "Shop.Infrastructure")]);
        graph.FindGodClassesAsync("Shop", 300, 3, Arg.Any<CancellationToken>())
             .Returns([(godClass, 700, 8)]);

        var result = await sut.FindArchitectureErosionTimelineAsync("Shop", days: 2);

        result.Should().Contain("## Architecture Erosion Timeline - Shop");
        result.Should().Contain("| Date | Cross-layer refs | Cycles | God classes | God-class lines |");
        result.Should().Contain("Current snapshot");
        result.Should().Contain("Cross-layer references: 1");
        result.Should().Contain("Circular namespace dependencies: 1");
        result.Should().Contain("God classes: 1");
        result.Should().Contain("God-class total lines: 700");
        result.Should().Contain("Resolved or deleted historical violations are not recoverable");
    }

    [Fact]
    public async Task FindArchitectureErosionTimelineAsync_WhenClean_ReturnsCleanMessage()
    {
        var (sut, graph) = Build();
        graph.FindArchitectureViolationsAsync(null, Arg.Any<CancellationToken>())
             .Returns([]);
        graph.FindCyclesAsync(null, Arg.Any<CancellationToken>())
             .Returns([]);
        graph.FindGodClassesAsync(null, 300, 3, Arg.Any<CancellationToken>())
             .Returns([]);

        var result = await sut.FindArchitectureErosionTimelineAsync(days: 30);

        result.Should().Contain("No architecture erosion signals found");
        result.Should().Contain("god-class thresholds are currently clean");
    }


}

