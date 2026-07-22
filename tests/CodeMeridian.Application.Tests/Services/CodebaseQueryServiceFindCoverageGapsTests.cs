using CodeMeridian.Application.Services;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace CodeMeridian.Application.Tests.Services;

public sealed class CodebaseQueryServiceFindCoverageGapsTests : CodebaseQueryServiceAnalyticsTestBase
{
    [Fact]
    public async Task FindCoverageGapsAsync_WhenEmpty_ReturnsAllCoveredMessage()
    {
        var (sut, graph) = Build();
        graph.FindCoverageGapsAsync(null, Arg.Any<CancellationToken>())
             .Returns([]);

        var result = await sut.FindCoverageGapsAsync();

        result.Should().Contain("No coverage gaps found");
        result.Should().Contain("All production classes and methods appear");
    }

    [Fact]
    public async Task FindCoverageGapsAsync_DefaultNoiseReduction_HidesLowPrioritySupportTypes()
    {
        var (sut, graph) = Build();
        graph.FindCoverageGapsAsync("MyApi", Arg.Any<CancellationToken>())
             .Returns([
                 Node("u1", "UntouchedService", CodeNodeType.Class, "src/Untouched.cs", project: "MyApi", lineCount: 20, fileRole: IndexedFileRole.Source),
                 Node("u2", "CheckoutResponse", CodeNodeType.Class, "src/CheckoutResponse.cs", 10, "MyApi", lineCount: 4, fileRole: IndexedFileRole.Source)
             ]);

        var result = await sut.FindCoverageGapsAsync("MyApi");

        result.Should().Contain("## Test Coverage Gaps");
        result.Should().Contain("**2** production");
        result.Should().Contain("### High-priority untested behavior (1)");
        result.Should().Contain("UntouchedService");
        result.Should().NotContain("### Low-priority support types");
        result.Should().NotContain("CheckoutResponse");
        result.Should().Contain("Hidden by default: 1 low-priority support type");
        result.Should().Contain("heuristic");
    }

    [Fact]
    public async Task FindCoverageGapsAsync_WhenBroaderOutputEnabled_ShowsLowPrioritySupportTypes()
    {
        var (sut, graph) = Build(new CodebaseAnalysisOptions
        {
            Ranking = new RankingOptions
            {
                IncludeBroaderHeuristicMatches = true
            }
        });
        graph.FindCoverageGapsAsync("MyApi", Arg.Any<CancellationToken>())
             .Returns([
                 Node("u1", "UntouchedService", CodeNodeType.Class, "src/Untouched.cs", project: "MyApi", lineCount: 20, fileRole: IndexedFileRole.Source),
                 Node("u2", "CheckoutResponse", CodeNodeType.Class, "src/CheckoutResponse.cs", 10, "MyApi", lineCount: 4, fileRole: IndexedFileRole.Source)
             ]);

        var result = await sut.FindCoverageGapsAsync("MyApi");

        result.Should().Contain("### High-priority untested behavior (1)");
        result.Should().Contain("### Low-priority support types (1)");
        result.Should().Contain("UntouchedService");
        result.Should().Contain("CheckoutResponse");
    }
    [Fact]
    public async Task FindCoverageGapsAsync_IncludesUnknownRoleNodesButFiltersGeneratedNodes()
    {
        var (sut, graph) = Build();
        graph.FindCoverageGapsAsync("CodeMeridian", Arg.Any<CancellationToken>())
             .Returns([
                 Node("u1", "LegacyService", CodeNodeType.Class, "src/LegacyService.cs", project: "CodeMeridian", fileRole: IndexedFileRole.Unknown),
                 Node("u2", "GeneratedMapper", CodeNodeType.Class, "src/Generated/Mapper.g.cs", project: "CodeMeridian", fileRole: IndexedFileRole.Generated)
             ]);

        var result = await sut.FindCoverageGapsAsync("CodeMeridian");

        result.Should().Contain("LegacyService");
        result.Should().NotContain("GeneratedMapper");
    }

    [Fact]
    public async Task FindCoverageGapsAsync_Summary_ReturnsHighAndLowPriorityCounts()
    {
        var (sut, graph) = Build();
        graph.FindCoverageGapsAsync("MyApi", Arg.Any<CancellationToken>())
             .Returns([
                 Node("u1", "UntouchedService", CodeNodeType.Class, "src/Untouched.cs", project: "MyApi", lineCount: 20, fileRole: IndexedFileRole.Source),
                 Node("u2", "CheckoutResponse", CodeNodeType.Class, "src/CheckoutResponse.cs", 10, "MyApi", lineCount: 4, fileRole: IndexedFileRole.Source)
             ]);

        var result = await sut.FindCoverageGapsAsync("MyApi", ContextDetailLevel.Summary);

        result.Should().Be("Coverage gap summary for 'MyApi': 1 high-priority, 1 low-priority.");
    }

    // ── FindRecentlyChangedAsync ──────────────────────────────────────────────


}

