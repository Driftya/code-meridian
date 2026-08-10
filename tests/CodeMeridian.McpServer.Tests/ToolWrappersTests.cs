using CodeMeridian.Application.Services;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;
using CodeMeridian.McpServer.Tools;
using FluentAssertions;
using ModelContextProtocol.Protocol;
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

    [Theory]
    [InlineData("too_long")]
    [InlineData("control")]
    public async Task CodebaseTools_QueryCodebaseAsync_RejectsInvalidProjectContext(string scenario)
    {
        var queryService = Substitute.For<ICodebaseQueryService>();
        var projectContext = scenario == "too_long"
            ? new string('x', 201)
            : "bad\u0001context";
        var sut = new CodebaseTools(queryService);

        var action = () => sut.QueryCodebaseAsync("query", projectContext);

        await action.Should().ThrowAsync<ArgumentException>();
        await queryService.DidNotReceiveWithAnyArgs().QueryStructureAsync(
            default!,
            default,
            default);
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
        queryService.FindImpactResultAsync("node-1", 4, true, Arg.Any<CancellationToken>())
            .Returns(new ImpactAnalysisResult(
                "1.0",
                "node-1",
                4,
                false,
                false,
                "High",
                new RelationshipCompletenessResult("High", "complete", null, null, 0, 0, 0, 0, 0, []),
                [],
                false));

        var sut = new CodebaseTools(queryService);
        var result = await sut.FindImpactAsync("node-1", 4, ContextDetailLevel.Full, includeConfidence: true);

        result.Content.OfType<TextContentBlock>().Should().ContainSingle()
            .Which.Text.Should().Contain("No callers found");
        result.StructuredContent.Should().NotBeNull();
        await queryService.Received(1).FindImpactResultAsync("node-1", 4, true, Arg.Any<CancellationToken>());
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

    [Theory]
    [InlineData("", "OrderService")]
    [InlineData("node-1", "   ")]
    public async Task KnowledgeTools_IngestCodeNodeAsync_RejectsBlankIdentity(string id, string name)
    {
        var codeGraph = Substitute.For<ICodeGraphRepository>();
        var vectorStore = Substitute.For<IVectorRepository>();
        var sut = new KnowledgeTools(codeGraph, vectorStore);

        var action = () => sut.IngestCodeNodeAsync(id, name, "Class");

        await action.Should().ThrowAsync<ArgumentException>();
        await codeGraph.DidNotReceiveWithAnyArgs().UpsertNodeAsync(default!, default);
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

    [Theory]
    [InlineData("", "target")]
    [InlineData("source", "   ")]
    public async Task KnowledgeTools_IngestRelationshipAsync_RejectsBlankEndpointIds(string sourceId, string targetId)
    {
        var codeGraph = Substitute.For<ICodeGraphRepository>();
        var vectorStore = Substitute.For<IVectorRepository>();
        var sut = new KnowledgeTools(codeGraph, vectorStore);

        var action = () => sut.IngestRelationshipAsync(sourceId, targetId, "Calls");

        await action.Should().ThrowAsync<ArgumentException>();
        await codeGraph.DidNotReceiveWithAnyArgs().UpsertEdgeAsync(default!, default);
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

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task KnowledgeTools_ClearProjectKnowledgeAsync_RejectsBlankProjectContext(string projectContext)
    {
        var codeGraph = Substitute.For<ICodeGraphRepository>();
        var vectorStore = Substitute.For<IVectorRepository>();
        var sut = new KnowledgeTools(codeGraph, vectorStore);

        var action = () => sut.ClearProjectKnowledgeAsync(projectContext);

        await action.Should().ThrowAsync<ArgumentException>()
            .WithParameterName(nameof(projectContext));
        await codeGraph.DidNotReceiveWithAnyArgs().DeleteProjectAsync(default!, default);
        await vectorStore.DidNotReceiveWithAnyArgs().DeleteProjectAsync(default!, default);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task KnowledgeTools_IngestDocumentAsync_RejectsBlankContent(string content)
    {
        var codeGraph = Substitute.For<ICodeGraphRepository>();
        var vectorStore = Substitute.For<IVectorRepository>();
        var sut = new KnowledgeTools(codeGraph, vectorStore);

        var action = () => sut.IngestDocumentAsync(content, projectContext: "CodeMeridian");

        await action.Should().ThrowAsync<ArgumentException>()
            .WithParameterName(nameof(content));
        await vectorStore.DidNotReceiveWithAnyArgs().UpsertAsync(default!, default);
    }

    [Fact]
    public async Task KnowledgeTools_IngestDocumentAsync_TreatsBlankOptionalValuesAsMissing()
    {
        var codeGraph = Substitute.For<ICodeGraphRepository>();
        var vectorStore = Substitute.For<IVectorRepository>();
        KnowledgeDocument? captured = null;
        vectorStore.UpsertAsync(
                Arg.Do<KnowledgeDocument>(document => captured = document),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var sut = new KnowledgeTools(codeGraph, vectorStore);

        await sut.IngestDocumentAsync("content", source: " ", projectContext: " ", id: " ");

        captured.Should().NotBeNull();
        captured!.Id.Should().NotBeNullOrWhiteSpace();
        captured.Source.Should().BeNull();
        captured.ProjectContext.Should().BeNull();
    }

    [Fact]
    public async Task ExtensionTools_LinkExternalConceptAsync_UpsertsNodeAndDirectionalEdge()
    {
        var codeGraph = Substitute.For<ICodeGraphRepository>();
        var sut = new ExtensionTools(codeGraph);

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
                node != null &&
                node.Id == "db:orders" &&
                node.Type == CodeNodeType.DatabaseTable &&
                node.ProjectContext == "CodeMeridian"),
            Arg.Any<CancellationToken>());
        await codeGraph.Received(1).UpsertEdgeAsync(
            Arg.Is<CodeEdge>(edge =>
                edge != null &&
                edge.SourceId == "db:orders" &&
                edge.TargetId == "Method:OrderService.SaveAsync" &&
                edge.Type == CodeEdgeType.Reads),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("", "db:orders", "orders table")]
    [InlineData("Method:OrderService.SaveAsync", "", "orders table")]
    [InlineData("Method:OrderService.SaveAsync", "db:orders", "   ")]
    public async Task ExtensionTools_LinkExternalConceptAsync_RejectsBlankRequiredValues(
        string codeNodeId,
        string externalConceptId,
        string externalConceptName)
    {
        var codeGraph = Substitute.For<ICodeGraphRepository>();
        var sut = new ExtensionTools(codeGraph);

        var action = () => sut.LinkExternalConceptAsync(codeNodeId, externalConceptId, externalConceptName);

        await action.Should().ThrowAsync<ArgumentException>();
        await codeGraph.DidNotReceiveWithAnyArgs().UpsertNodeAsync(default!, default);
        await codeGraph.DidNotReceiveWithAnyArgs().UpsertEdgeAsync(default!, default);
    }

    [Theory]
    [InlineData("Nope", "outgoing", "Unknown relationship type")]
    [InlineData("Reads", "sideways", "Unknown direction")]
    public async Task ExtensionTools_LinkExternalConceptAsync_RejectsInvalidEdgeOptionsBeforeMutation(
        string relationshipType,
        string direction,
        string expectedMessage)
    {
        var codeGraph = Substitute.For<ICodeGraphRepository>();
        var sut = new ExtensionTools(codeGraph);

        var result = await sut.LinkExternalConceptAsync(
            "Method:OrderService.SaveAsync",
            "db:orders",
            "orders table",
            relationshipType: relationshipType,
            direction: direction);

        result.Should().Contain(expectedMessage);
        await codeGraph.DidNotReceiveWithAnyArgs().UpsertNodeAsync(default!, default);
        await codeGraph.DidNotReceiveWithAnyArgs().UpsertEdgeAsync(default!, default);
    }
}
