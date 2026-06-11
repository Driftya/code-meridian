using System.Reflection;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;
using CodeMeridian.McpServer.Api;
using CodeMeridian.McpServer.Tools;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using NSubstitute;

namespace CodeMeridian.McpServer.Tests;

public sealed class KnowledgeIngestionTests
{
    [Fact]
    public async Task KnowledgeTools_IngestDocumentAsync_ForwardsWeakMentionMetadata()
    {
        var graph = Substitute.For<ICodeGraphRepository>();
        var vector = Substitute.For<IVectorRepository>();
        KnowledgeDocument? captured = null;

        vector
            .UpsertAsync(Arg.Do<KnowledgeDocument>(doc => captured = doc), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sut = new KnowledgeTools(graph, vector);

        await sut.IngestDocumentAsync(
            content: "Implementation notes",
            source: "docs/notes.md",
            projectContext: "CodeMeridian",
            id: "doc-2",
            relatedNodeIdsCsv: "Class:CodeMeridian.McpServer.Tools.CodebaseTools");

        captured.Should().NotBeNull();
        captured!.Metadata.Should().ContainKey("relatedNodeIds");
        captured.Metadata["relatedNodeIds"].Should().Be("Class:CodeMeridian.McpServer.Tools.CodebaseTools");
        captured.ProjectContext.Should().Be("CodeMeridian");
    }

    [Fact]
    public void KnowledgeApiEndpoints_ParseMetadata_StoresRelatedNodeIdsAsPrimitiveProperty()
    {
        var method = typeof(KnowledgeApiEndpoints).GetMethod(
            "ParseMetadata",
            BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull();

        var result = (Dictionary<string, string>)method!.Invoke(null, new object?[] { "Method:Foo.Bar,Method:Baz.Qux", null })!;

        result.Should().ContainKey("relatedNodeIds");
        result["relatedNodeIds"].Should().Be("Method:Foo.Bar,Method:Baz.Qux");
    }

    [Fact]
    public async Task KnowledgeApiEndpoints_IngestEdge_ForwardsEdgeMetadata()
    {
        var repo = Substitute.For<ICodeGraphRepository>();
        var routeHandler = typeof(KnowledgeApiEndpoints)
            .GetMethod("IngestEdge", BindingFlags.NonPublic | BindingFlags.Static);

        routeHandler.Should().NotBeNull();

        var requestType = typeof(KnowledgeApiEndpoints).Assembly.GetType("CodeMeridian.McpServer.Api.IngestEdgeRequest");
        requestType.Should().NotBeNull();

        var request = Activator.CreateInstance(
            requestType!,
            "frontend",
            "endpoint",
            "Calls",
            true,
            "src/app.ts:42",
            2,
            0.9);

        var task = (Task<IResult>)routeHandler!.Invoke(null, [request, repo, CancellationToken.None])!;
        await task;

        await repo.Received(1).UpsertEdgeAsync(
            Arg.Is<CodeEdge>(edge =>
                edge.SourceId == "frontend" &&
                edge.TargetId == "endpoint" &&
                edge.Type == CodeEdgeType.Calls &&
                edge.IsAsync == true &&
                edge.CallSite == "src/app.ts:42" &&
                edge.ParamCount == 2 &&
                edge.Confidence == 0.9),
            Arg.Any<CancellationToken>());
    }
}
