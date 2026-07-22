using CodeMeridian.Application.Services;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace CodeMeridian.Application.Tests.Services;

public sealed class CodebaseQueryServiceFindImpactTests : CodebaseQueryServiceAnalyticsTestBase
{
    [Fact]
    public async Task FindImpactAsync_WhenNoCallers_ReturnsGuidanceMessage()
    {
        var (sut, graph) = Build();
        graph.FindImpactAsync("Method:Foo.Bar()", 5, Arg.Any<CancellationToken>())
             .Returns([]);

        var result = await sut.FindImpactAsync("Method:Foo.Bar()");

        result.Should().Contain("No callers found");
        result.Should().Contain("Method:Foo.Bar()");
    }

    [Fact]
    public async Task FindImpactAsync_WithCallers_ReturnsMarkdownTable()
    {
        var (sut, graph) = Build();
        graph.FindImpactAsync("Method:Foo.Bar()", 3, Arg.Any<CancellationToken>())
             .Returns([(Node("c1", "Caller", CodeNodeType.Method, "src/Caller.cs"), 1),
                       (Node("c2", "Indirect", CodeNodeType.Class), 2)]);

        var result = await sut.FindImpactAsync("Method:Foo.Bar()", depth: 3);

        result.Should().Contain("## Impact Analysis");
        result.Should().Contain("**2** code elements");
        result.Should().Contain("Caller");
        result.Should().Contain("src/Caller.cs");
        result.Should().Contain("| 1 |");
        result.Should().Contain("| 2 |");
        result.Should().Contain("—"); // missing FilePath rendered as dash
    }

    [Fact]
    public async Task FindImpactAsync_ForClassTarget_WithExpandedEvidence_RendersCallerBuckets()
    {
        var (sut, graph) = Build();
        const string target = "CodeMeridian::Class::Shop.OrderWorkflow";
        var directCaller = Node("class-1", "OrdersController", CodeNodeType.Class, "src/Api/OrdersController.cs") with
        {
            Properties = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["impactEvidenceBucket"] = "direct-class"
            }
        };
        var memberCaller = Node("method-1", "CheckoutOrchestrator.Run", CodeNodeType.Method, "src/App/CheckoutOrchestrator.cs") with
        {
            Properties = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["impactEvidenceBucket"] = "member"
            }
        };
        var dependencyCaller = Node("class-2", "OrderWorkflowFactory", CodeNodeType.Class, "src/App/OrderWorkflowFactory.cs") with
        {
            Properties = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["impactEvidenceBucket"] = "dependency"
            }
        };
        var workflowCaller = Node("api-1", "POST /api/orders", CodeNodeType.ApiEndpoint, "src/Api/OrdersEndpoints.cs") with
        {
            Properties = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["impactEvidenceBucket"] = "workflow"
            }
        };

        graph.FindImpactAsync(target, 5, Arg.Any<CancellationToken>())
             .Returns([
                 (directCaller, 1),
                 (memberCaller, 2),
                 (dependencyCaller, 1),
                 (workflowCaller, 2)
             ]);

        var result = await sut.FindImpactAsync(target);

        result.Should().Contain("## Impact Analysis");
        result.Should().Contain("Caller evidence");
        result.Should().Contain("### Direct class callers (1)");
        result.Should().Contain("### Member callers (1)");
        result.Should().Contain("### Dependency/composition callers (1)");
        result.Should().Contain("### Workflow-adjacent callers (1)");
        result.Should().Contain("OrdersController");
        result.Should().Contain("CheckoutOrchestrator.Run");
        result.Should().Contain("OrderWorkflowFactory");
        result.Should().Contain("POST /api/orders");
        result.Should().NotContain("No callers found");
    }

    // ── FindHotspotsAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task FindImpactAsync_WithConfidence_SeparatesProvenHeuristicAndUnknownRisk()
    {
        var (sut, graph) = Build();
        var target = Node("target", "ChargeAsync", CodeNodeType.Method, "src/Payments/PaymentGateway.cs", 42, "Shop", updatedAt: DateTimeOffset.UtcNow);
        var provenCaller = Node("caller-1", "CheckoutService.PlaceOrder", CodeNodeType.Method, "src/Checkout/CheckoutService.cs", 18, "Shop", updatedAt: DateTimeOffset.UtcNow);
        var routeNode = Node("route-1", "POST /api/payments", CodeNodeType.ApiEndpoint, "src/Api/PaymentsEndpoint.cs", 9, "Shop.Api", updatedAt: DateTimeOffset.UtcNow);
        var staleCaller = Node("caller-2", "LegacyBatchJob.Run", CodeNodeType.Method, project: "Shop.Legacy");

        graph.FindImpactPathsAsync("Method:Payments.PaymentGateway.ChargeAsync", 3, Arg.Any<CancellationToken>())
             .Returns([
                 new ImpactPath(
                     provenCaller,
                     1,
                     [
                         new GraphPathStep(provenCaller, "Calls", null),
                         new GraphPathStep(target, null, null)
                     ]),
                 new ImpactPath(
                     routeNode,
                     2,
                     [
                         new GraphPathStep(routeNode, "Calls", null),
                         new GraphPathStep(target, null, null)
                     ]),
                 new ImpactPath(
                     staleCaller,
                     1,
                     [
                         new GraphPathStep(staleCaller, "Calls", null),
                         new GraphPathStep(target, null, null)
                     ])
             ]);

        var result = await sut.FindImpactAsync(
            "Method:Payments.PaymentGateway.ChargeAsync",
            depth: 3,
            includeConfidence: true);

        result.Should().Contain("## Impact Analysis");
        result.Should().Contain("**Impact confidence:** Low");
        result.Should().Contain("### Proven callers (1)");
        result.Should().Contain("CheckoutService.PlaceOrder");
        result.Should().Contain("direct structural path");
        result.Should().Contain("### Heuristic callers (1)");
        result.Should().Contain("POST /api/payments");
        result.Should().Contain("path crosses route or knowledge nodes");
        result.Should().Contain("### Unknown risk (1)");
        result.Should().Contain("LegacyBatchJob.Run");
        result.Should().Contain("node is missing update metadata");
    }

    [Fact]
    public async Task FindImpactAsync_WithConfidenceSummary_ReturnsConfidenceCounts()
    {
        var (sut, graph) = Build();
        var target = Node("target", "ChargeAsync", CodeNodeType.Method, "src/Payments/PaymentGateway.cs", 42, "Shop", updatedAt: DateTimeOffset.UtcNow);
        var provenCaller = Node("caller-1", "CheckoutService.PlaceOrder", CodeNodeType.Method, "src/Checkout/CheckoutService.cs", 18, "Shop", updatedAt: DateTimeOffset.UtcNow);
        var heuristicCaller = Node("route-1", "POST /api/payments", CodeNodeType.ApiEndpoint, "src/Api/PaymentsEndpoint.cs", 9, "Shop.Api", updatedAt: DateTimeOffset.UtcNow);

        graph.FindImpactPathsAsync("Method:Payments.PaymentGateway.ChargeAsync", 3, Arg.Any<CancellationToken>())
             .Returns([
                 new ImpactPath(
                     provenCaller,
                     1,
                     [
                         new GraphPathStep(provenCaller, "Calls", null),
                         new GraphPathStep(target, null, null)
                     ]),
                 new ImpactPath(
                     heuristicCaller,
                     2,
                     [
                         new GraphPathStep(heuristicCaller, "Calls", null),
                         new GraphPathStep(target, null, null)
                     ])
             ]);

        var result = await sut.FindImpactAsync(
            "Method:Payments.PaymentGateway.ChargeAsync",
            depth: 3,
            detailLevel: ContextDetailLevel.Summary,
            includeConfidence: true);

        result.Should().Be(
            "Impact summary for `Method:Payments.PaymentGateway.ChargeAsync`: 2 affected code elements within 3 hops. " +
            "Confidence: Medium. 1 proven, 1 heuristic, 0 unknown risk.");
    }

    [Fact]
    public async Task FindImpactAsync_WithConfidence_WhenNoCallers_ReturnsGuidanceMessage()
    {
        var (sut, graph) = Build();
        graph.FindImpactPathsAsync("Method:Foo.Bar()", 5, Arg.Any<CancellationToken>())
             .Returns([]);

        var result = await sut.FindImpactAsync("Method:Foo.Bar()", includeConfidence: true);

        result.Should().StartWith(
            "No callers found for `Method:Foo.Bar()` within 5 hops. " +
            "The node may not exist in the graph or has no inbound dependencies.");
        result.Should().Contain("An empty relationship result is not proof that a change is safe");
    }


}

