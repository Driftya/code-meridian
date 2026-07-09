using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CodeMeridian.Application.Extensions;
using CodeMeridian.Application.Services;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;
using CodeMeridian.McpServer.Tools;
using CodeMeridian.Sdk.Models;
using FluentAssertions;
using NSubstitute;

namespace CodeMeridian.McpServer.Tests;

public sealed class ToolWrappersTests
{
    [Fact]
    public async Task CodebaseTools_QueryCodebaseAsync_ForwardsArguments()
    {
        var queryService = Substitute.For<ICodebaseQueryService>();
        queryService.QueryStructureAsync("callers of SaveAsync", "CodeMeridian", Arg.Any<CancellationToken>())
            .Returns("query-result");

        var sut = new CodebaseTools(queryService);
        var result = await sut.QueryCodebaseAsync("callers of SaveAsync", "CodeMeridian");

        result.Should().Be("query-result");
        await queryService.Received(1).QueryStructureAsync("callers of SaveAsync", "CodeMeridian", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CodebaseTools_GetArchitecturalOverviewAsync_ForwardsArguments()
    {
        var queryService = Substitute.For<ICodebaseQueryService>();
        queryService.GetOverviewAsync("CodeMeridian", Arg.Any<CancellationToken>())
            .Returns("overview");

        var sut = new CodebaseTools(queryService);
        var result = await sut.GetArchitecturalOverviewAsync("CodeMeridian");

        result.Should().Be("overview");
        await queryService.Received(1).GetOverviewAsync("CodeMeridian", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CodebaseTools_SearchDocumentationAsync_ForwardsArguments()
    {
        var queryService = Substitute.For<ICodebaseQueryService>();
        queryService.SearchDocumentationAsync("keyword graph", "CodeMeridian", Arg.Any<CancellationToken>())
            .Returns("docs");

        var sut = new CodebaseTools(queryService);
        var result = await sut.SearchDocumentationAsync("keyword graph", "CodeMeridian");

        result.Should().Be("docs");
        await queryService.Received(1).SearchDocumentationAsync("keyword graph", "CodeMeridian", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CodebaseTools_FindImpactAsync_ForwardsArguments()
    {
        var queryService = Substitute.For<ICodebaseQueryService>();
        queryService.FindImpactAsync("node-1", 4, ContextDetailLevel.Full, true, Arg.Any<CancellationToken>())
            .Returns("impact");

        var sut = new CodebaseTools(queryService);
        var result = await sut.FindImpactAsync("node-1", 4, ContextDetailLevel.Full, includeConfidence: true);

        result.Should().Be("impact");
        await queryService.Received(1).FindImpactAsync("node-1", 4, ContextDetailLevel.Full, true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CodebaseTools_FindDiagnosticsMethods_ForwardArguments()
    {
        var queryService = Substitute.For<ICodebaseQueryService>();
        queryService.FindDiagnosticsAsync("CodeMeridian", "warning", Arg.Any<CancellationToken>())
            .Returns("diagnostics");
        queryService.FindDiagnosticsForNodeAsync("node-1", Arg.Any<CancellationToken>())
            .Returns("nearby");

        var sut = new CodebaseTools(queryService);

        var diagnostics = await sut.FindDiagnosticsAsync("CodeMeridian", "warning");
        var nearby = await sut.FindDiagnosticsForNodeAsync("node-1");

        diagnostics.Should().Be("diagnostics");
        nearby.Should().Be("nearby");
        await queryService.Received(1).FindDiagnosticsAsync("CodeMeridian", "warning", Arg.Any<CancellationToken>());
        await queryService.Received(1).FindDiagnosticsForNodeAsync("node-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CodebaseTools_PlanningMethods_ForwardArguments()
    {
        var queryService = Substitute.For<ICodebaseQueryService>();
        queryService.FindImplementationSurfaceAsync("add stale knowledge query", "knowledge,document", "CodeMeridian", 8, Arg.Any<CancellationToken>())
            .Returns("surface");
        queryService.AnalyzeFeatureImplementationPathAsync("docs/features/20.md", "CodeMeridian", false, true, false, 6, Arg.Any<CancellationToken>())
            .Returns("feature");
        queryService.PlanEditRouteAsync("replace dependency", "json,contracts", "CodeMeridian", 5, Arg.Any<CancellationToken>())
            .Returns("route");
        queryService.FindGraphDriftAsync("CodeMeridian", 9, Arg.Any<CancellationToken>())
            .Returns("drift");

        var sut = new CodebaseTools(queryService);

        (await sut.FindImplementationSurfaceAsync("add stale knowledge query", "knowledge,document", "CodeMeridian", 8)).Should().Be("surface");
        (await sut.AnalyzeFeatureImplementationPathAsync("docs/features/20.md", "CodeMeridian", includeTests: false, includeDocs: true, includeRisk: false, limit: 6)).Should().Be("feature");
        (await sut.PlanEditRouteAsync("replace dependency", "json,contracts", "CodeMeridian", 5)).Should().Be("route");
        (await sut.FindGraphDriftAsync("CodeMeridian", 9)).Should().Be("drift");
    }

    [Fact]
    public async Task KnowledgeTools_IngestCodeNodeAsync_ParsesEmbeddingAndUpsertsNode()
    {
        var codeGraph = Substitute.For<ICodeGraphRepository>();
        var vectorStore = Substitute.For<IVectorRepository>();
        CodeNode? captured = null;
        codeGraph.UpsertNodeAsync(Arg.Do<CodeNode>(node => captured = node), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sut = new KnowledgeTools(codeGraph, vectorStore);
        var result = await sut.IngestCodeNodeAsync(
            "node-1",
            "OrderService",
            "Class",
            namespacePath: "Shop",
            filePath: "src/OrderService.cs",
            lineNumber: 12,
            lineCount: 40,
            summary: "Handles orders",
            sourceSnippet: "class OrderService {}",
            sourceHash: "abc123",
            projectContext: "CodeMeridian",
            embeddingCsv: "1, 2, -3");

        result.Should().Contain("3-dim embedding");
        captured.Should().NotBeNull();
        captured!.Type.Should().Be(CodeNodeType.Class);
        captured.Embedding.Should().Equal([1f, 2f, -3f]);
        captured.ProjectContext.Should().Be("CodeMeridian");
    }

    [Fact]
    public async Task KnowledgeTools_IngestCodeNodeAsync_RejectsUnknownTypeAndMalformedEmbedding()
    {
        var codeGraph = Substitute.For<ICodeGraphRepository>();
        var vectorStore = Substitute.For<IVectorRepository>();
        var sut = new KnowledgeTools(codeGraph, vectorStore);

        (await sut.IngestCodeNodeAsync("node-1", "OrderService", "NotAType"))
            .Should().Contain("Unknown node type");
        (await sut.IngestCodeNodeAsync("node-1", "OrderService", "Class", embeddingCsv: "a,b,c"))
            .Should().Contain("Invalid embeddingCsv format");

        await codeGraph.DidNotReceive().UpsertNodeAsync(Arg.Any<CodeNode>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task KnowledgeTools_IngestRelationshipAsync_ValidatesRelationshipType()
    {
        var codeGraph = Substitute.For<ICodeGraphRepository>();
        var vectorStore = Substitute.For<IVectorRepository>();
        var sut = new KnowledgeTools(codeGraph, vectorStore);

        var result = await sut.IngestRelationshipAsync("source", "target", "Nope");

        result.Should().Contain("Unknown relationship type");
        await codeGraph.DidNotReceive().UpsertEdgeAsync(Arg.Any<CodeEdge>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task KnowledgeTools_ClearMethods_UseExpectedRepositories()
    {
        var codeGraph = Substitute.For<ICodeGraphRepository>();
        var vectorStore = Substitute.For<IVectorRepository>();
        var sut = new KnowledgeTools(codeGraph, vectorStore);

        (await sut.ClearCodeGraphAsync()).Should().Contain("confirm=true");
        (await sut.ClearCodeGraphAsync(confirm: true)).Should().Contain("removed from Neo4j");
        (await sut.ClearProjectKnowledgeAsync("CodeMeridian")).Should().Contain("CodeMeridian");

        await codeGraph.Received(1).DeleteAllAsync(Arg.Any<CancellationToken>());
        await codeGraph.Received(1).DeleteProjectAsync("CodeMeridian", Arg.Any<CancellationToken>());
        await vectorStore.Received(1).DeleteProjectAsync("CodeMeridian", Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ExtensionTools_ListRegisterAndUnregisterAgents_Works()
    {
        var registry = new ExtensionRegistry();
        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        var codeGraph = Substitute.For<ICodeGraphRepository>();
        var sut = new ExtensionTools(registry, httpClientFactory, codeGraph);

        sut.ListProjectAgents().Should().Contain("No project agents registered");
        sut.RegisterProjectAgent("Payments", "Billing helper", "https://payments.example/ask", "payments, invoices")
            .Should().Contain("Agent 'Payments' registered");

        var listed = sut.ListProjectAgents();
        listed.Should().Contain("Payments");
        listed.Should().Contain("Billing helper");
        listed.Should().Contain("payments, invoices");

        sut.UnregisterProjectAgent("Payments").Should().Contain("unregistered");
        sut.ListProjectAgents().Should().Contain("No project agents registered");
        sut.RegisterProjectAgent("Broken", "Bad", "not-a-url", "oops").Should().Contain("Invalid endpoint URL");
    }

    [Fact]
    public async Task ExtensionTools_CallProjectAgentAsync_HandlesSuccessFailureAndMissingAgent()
    {
        var registry = new ExtensionRegistry();
        var codeGraph = Substitute.For<ICodeGraphRepository>();
        using var successHandler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new AgentResponse { Content = "agent-result", AgentName = "Payments" })
        });
        var successFactory = Substitute.For<IHttpClientFactory>();
        successFactory.CreateClient("CodeMeridianExtension").Returns(new HttpClient(successHandler));

        var successTools = new ExtensionTools(registry, successFactory, codeGraph);
        successTools.RegisterProjectAgent("Payments", "Billing helper", "https://payments.example/ask", "payments");

        (await successTools.CallProjectAgentAsync("Missing", "hello")).Should().Contain("not found");
        (await successTools.CallProjectAgentAsync("Payments", "hello", "CodeMeridian")).Should().Be("agent-result");

        var request = successHandler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Post);
        request.Uri!.ToString().Should().Be("https://payments.example/ask");
        using (var body = JsonDocument.Parse(request.Body!))
        {
            body.RootElement.GetProperty("query").GetString().Should().Be("hello");
            body.RootElement.GetProperty("projectContext").GetString().Should().Be("CodeMeridian");
        }

        using var failingHandler = new StubHttpHandler(_ => throw new HttpRequestException("boom"));
        var failingFactory = Substitute.For<IHttpClientFactory>();
        failingFactory.CreateClient("CodeMeridianExtension").Returns(new HttpClient(failingHandler));
        var failingTools = new ExtensionTools(registry, failingFactory, codeGraph);

        (await failingTools.CallProjectAgentAsync("Payments", "hello")).Should().Contain("call failed: boom");
        registry.Get("Payments")!.IsHealthy.Should().BeFalse();
    }

    [Fact]
    public async Task ExtensionTools_LinkExternalConceptAsync_UpsertsNodeAndDirectionalEdge()
    {
        var registry = new ExtensionRegistry();
        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        var codeGraph = Substitute.For<ICodeGraphRepository>();
        var sut = new ExtensionTools(registry, httpClientFactory, codeGraph);

        var result = await sut.LinkExternalConceptAsync(
            "Method:OrderService.SaveAsync",
            "db:orders",
            "orders table",
            conceptType: "DatabaseTable",
            relationshipType: "Reads",
            direction: "incoming",
            projectContext: "CodeMeridian");

        result.Should().Contain("db:orders");
        await codeGraph.Received(1).UpsertNodeAsync(
            Arg.Is<CodeNode>(node =>
                node.Id == "db:orders" &&
                node.Type == CodeNodeType.DatabaseTable &&
                node.ProjectContext == "CodeMeridian"),
            Arg.Any<CancellationToken>());
        await codeGraph.Received(1).UpsertEdgeAsync(
            Arg.Is<CodeEdge>(edge =>
                edge.SourceId == "db:orders" &&
                edge.TargetId == "Method:OrderService.SaveAsync" &&
                edge.Type == CodeEdgeType.Reads),
            Arg.Any<CancellationToken>());
    }

    private sealed class StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler, IDisposable
    {
        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new CapturedRequest(request.Method, request.RequestUri, body));
            return responder(request);
        }
    }

    private sealed record CapturedRequest(HttpMethod Method, Uri? Uri, string? Body);
}
