using CodeMeridian.Application.Services;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace CodeMeridian.Application.Tests.Services;

public sealed class CodebaseQueryServiceFindBridgesTests : CodebaseQueryServiceAnalyticsTestBase
{
    [Fact]
    public async Task FindBridgesAsync_WhenGdsFails_ReturnsInstallGuidance()
    {
        var (sut, graph) = Build();
        graph.GetBetweennessAsync(null, Arg.Any<int>(), Arg.Any<CancellationToken>())
             .ThrowsAsync(new InvalidOperationException("No such procedure: gds.betweenness.stream"));
        graph.GetPageRankAsync(null, Arg.Any<int>(), Arg.Any<CancellationToken>())
             .Returns([]);
        graph.GetArticulationPointsAsync(null, Arg.Any<int>(), Arg.Any<CancellationToken>())
             .Returns([]);
        graph.GetBridgeEdgesAsync(null, Arg.Any<int>(), Arg.Any<CancellationToken>())
             .Returns([]);

        var result = await sut.FindBridgesAsync();

        result.Should().Contain("Risky core analysis failed");
        result.Should().Contain("Graph Data Science");
    }

    [Fact]
    public async Task FindBridgesAsync_WithResults_ReturnsRiskyCoreSignalsAndBridgeEdges()
    {
        var (sut, graph) = Build();
        var bridge = Node(
            "b1",
            "PaymentFacade",
            CodeNodeType.Class,
            "src/Application/Payments/PaymentFacade.cs",
            line: 12,
            @namespace: "CodeMeridian.Application.Payments",
            project: "CodeMeridian",
            updatedAt: DateTimeOffset.UtcNow,
            sourceHash: "abc123");
        var apiCaller = Node("c1", "PaymentsController", CodeNodeType.Class, "src/Api/PaymentsController.cs", @namespace: "CodeMeridian.Api.Payments");
        var infraCallee = Node("d1", "StripeGateway", CodeNodeType.Class, "src/Infrastructure/Payments/StripeGateway.cs", @namespace: "CodeMeridian.Infrastructure.Payments");

        graph.GetBetweennessAsync(null, Arg.Any<int>(), Arg.Any<CancellationToken>())
             .Returns([(bridge, 980.0)]);
        graph.GetPageRankAsync(null, Arg.Any<int>(), Arg.Any<CancellationToken>())
             .Returns([(bridge, 0.88)]);
        graph.GetArticulationPointsAsync(null, Arg.Any<int>(), Arg.Any<CancellationToken>())
             .Returns([(bridge, 3)]);
        graph.GetBridgeEdgesAsync(null, Arg.Any<int>(), Arg.Any<CancellationToken>())
             .Returns([
                 (apiCaller, bridge, (IReadOnlyList<long>)new long[] { 5, 4 }),
                 (bridge, infraCallee, (IReadOnlyList<long>)new long[] { 6, 3 })
             ]);
        graph.GetContextForEditingAsync("b1", Arg.Any<CancellationToken>())
             .Returns(new EditingContext(bridge, [apiCaller], [infraCallee], []));

        var result = await sut.FindBridgesAsync();

        result.Should().Contain("## Risky Core Nodes");
        result.Should().Contain("PaymentFacade");
        result.Should().Contain("API -> Application -> Infrastructure");
        result.Should().Contain("splits graph into 3 component(s)");
        result.Should().Contain("touches 2 bridge edge(s)");
        result.Should().Contain("find_impact");
        result.Should().Contain("### Bridge edges");
    }

    [Fact]
    public async Task FindBridgesAsync_ExcludesTestsAndDocumentationFromRankedCandidates()
    {
        var (sut, graph) = Build();
        var bridge = Node(
            "prod:bridge",
            "PaymentFacade",
            CodeNodeType.Class,
            "src/Application/Payments/PaymentFacade.cs",
            line: 12,
            project: "CodeMeridian",
            updatedAt: DateTimeOffset.UtcNow,
            sourceHash: "prod-hash",
            @namespace: "CodeMeridian.Application.Payments");
        var infra = Node(
            "prod:infra",
            "StripeGateway",
            CodeNodeType.Class,
            "src/Infrastructure/Payments/StripeGateway.cs",
            line: 20,
            project: "CodeMeridian",
            updatedAt: DateTimeOffset.UtcNow,
            sourceHash: "infra-hash",
            @namespace: "CodeMeridian.Infrastructure.Payments");
        var testHelper = Node(
            "test:build",
            "Build()",
            CodeNodeType.Method,
            "tests/CodeMeridian.Application.Tests/Services/PaymentFacadeTests.cs",
            line: 8,
            project: "CodeMeridian",
            updatedAt: DateTimeOffset.UtcNow,
            sourceHash: "test-hash",
            fileRole: IndexedFileRole.Test,
            @namespace: "CodeMeridian.Application.Tests.Services");
        var docAsset = Node(
            "doc:file",
            "docs-index.css",
            CodeNodeType.File,
            "docs/assets/docs-index.css",
            line: 1,
            project: "CodeMeridian",
            updatedAt: DateTimeOffset.UtcNow,
            sourceHash: "doc-hash");

        graph.GetBetweennessAsync(null, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([
                (bridge, 900.0),
                (testHelper, 950.0),
                (docAsset, 920.0)
            ]);
        graph.GetPageRankAsync(null, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([
                (bridge, 0.82),
                (testHelper, 0.91),
                (docAsset, 0.88)
            ]);
        graph.GetArticulationPointsAsync(null, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([
                (bridge, 4),
                (testHelper, 5),
                (docAsset, 3)
            ]);
        graph.GetBridgeEdgesAsync(null, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([
                (testHelper, bridge, (IReadOnlyList<long>)new long[] { 9, 4 }),
                (docAsset, bridge, (IReadOnlyList<long>)new long[] { 8, 3 }),
                (bridge, infra, (IReadOnlyList<long>)new long[] { 6, 2 })
            ]);
        graph.GetContextForEditingAsync(bridge.Id, Arg.Any<CancellationToken>())
            .Returns(new EditingContext(bridge, [], [infra], []));
        graph.GetContextForEditingAsync(infra.Id, Arg.Any<CancellationToken>())
            .Returns(new EditingContext(infra, [bridge], [], []));

        var result = await sut.FindBridgesAsync();

        result.Should().Contain("PaymentFacade");
        result.Should().NotContain("Build()");
        result.Should().NotContain("docs-index.css");
    }


}

