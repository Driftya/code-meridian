using CodeMeridian.Application.Services;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace CodeMeridian.Application.Tests.Services;

public sealed class CodebaseQueryServiceFindGodClassesTests : CodebaseQueryServiceAnalyticsTestBase
{
    [Fact]
    public async Task FindGodClassesAsync_WhenEmpty_ReturnsGuidanceMessage()
    {
        var (sut, graph) = Build();
        graph.FindGodClassesAsync("Proj", Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
             .Returns([]);

        var result = await sut.FindGodClassesAsync("Proj");

        result.Should().Contain("No god classes found");
        result.Should().Contain("re-index");
    }

    [Fact]
    public async Task FindGodClassesAsync_WithCriticalClass_ReturnsRiskTable()
    {
        var (sut, graph) = Build();
        var godClass = NodeWithLineCount("g1", "MegaService", CodeNodeType.Class, "src/Mega.cs", lineCount: 800);

        graph.FindGodClassesAsync(null, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
             .Returns([(godClass, 800, 40)]);

        var result = await sut.FindGodClassesAsync();

        result.Should().Contain("## God Classes");
        result.Should().Contain("Critical");
        result.Should().Contain("800");
        result.Should().Contain("40");
        result.Should().Contain("`MegaService`");
        result.Should().Contain("get_context_for_editing");
    }

    [Fact]
    public async Task FindGodClassesAsync_RendersCallerEvidenceBreakdownAndIndirectWarning()
    {
        var (sut, graph) = Build();
        var directHeavy = NodeWithLineCount("g1", "OrderWorkflow", CodeNodeType.Class, "src/OrderWorkflow.cs", lineCount: 360) with
        {
            Properties = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["godClassDirectCallerCount"] = "2",
                ["godClassMemberCallerCount"] = "3",
                ["godClassDependencyCallerCount"] = "1",
                ["godClassHeuristicCallerCount"] = "0",
                ["godClassQualityScore"] = "95"
            }
        };
        var indirectHeavy = NodeWithLineCount("g2", "BroadCoordinator", CodeNodeType.Class, "src/BroadCoordinator.cs", lineCount: 420) with
        {
            Properties = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["godClassDirectCallerCount"] = "0",
                ["godClassMemberCallerCount"] = "0",
                ["godClassDependencyCallerCount"] = "1",
                ["godClassHeuristicCallerCount"] = "4",
                ["godClassQualityScore"] = "34"
            }
        };

        graph.FindGodClassesAsync(null, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
             .Returns([
                 (directHeavy, 360, 6),
                 (indirectHeavy, 420, 5)
             ]);

        var result = await sut.FindGodClassesAsync();

        result.Should().Contain("Caller evidence");
        result.Should().Contain("2 direct, 3 member, 1 dependency, 0 heuristic");
        result.Should().Contain("mostly indirect");
        result.Should().Contain("0 direct, 0 member, 1 dependency, 4 heuristic");
        result.Should().Contain("ranked by caller quality and size");
    }

    [Fact]
    public async Task FindGodClassesAsync_WithMultipleRisks_RendersAllRows()
    {
        var (sut, graph) = Build();
        var c1 = NodeWithLineCount("g1", "CriticalSvc", CodeNodeType.Class, lineCount: 500);
        var c2 = NodeWithLineCount("g2", "MediumSvc",   CodeNodeType.Class, lineCount: 350);

        graph.FindGodClassesAsync(null, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
             .Returns([(c1, 500, 40), (c2, 350, 4)]);

        var result = await sut.FindGodClassesAsync();

        result.Should().Contain("CriticalSvc");
        result.Should().Contain("MediumSvc");
        result.Should().Contain("Critical");
        result.Should().Contain("Medium");
    }

    [Fact]
    public async Task FindGodClassesAsync_FiltersTestsAndMigrationsByDefault()
    {
        var (sut, graph) = Build();

        graph.FindGodClassesAsync(null, Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
             .Returns([
                 (Node("source", "OrderService", CodeNodeType.Class, "src/OrderService.cs", lineCount: 420, fileRole: IndexedFileRole.Source), 420, 6),
                 (Node("test", "OrderServiceTests", CodeNodeType.Class, "tests/OrderServiceTests.cs", lineCount: 600, fileRole: IndexedFileRole.Test), 600, 10),
                 (Node("migration", "CreateUsers", CodeNodeType.Class, "src/Migrations/CreateUsers.cs", lineCount: 700, fileRole: IndexedFileRole.Migration), 700, 12)
             ]);

        var result = await sut.FindGodClassesAsync();

        result.Should().Contain("OrderService");
        result.Should().NotContain("OrderServiceTests");
        result.Should().NotContain("CreateUsers");
    }

    // ── Factory helpers for line-count nodes ──────────────────────────────────

    // ── FindDownstreamAsync ───────────────────────────────────────────────────


}

