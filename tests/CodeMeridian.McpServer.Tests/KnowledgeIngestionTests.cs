using System.Reflection;
using CodeMeridian.Application.Services;
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
    public async Task CodebaseTools_FindBridgesAsync_ForwardsProjectContext()
    {
        var queryService = Substitute.For<ICodebaseQueryService>();
        queryService.FindBridgesAsync("CodeMeridian", Arg.Any<CancellationToken>())
            .Returns("bridges");

        var sut = new CodebaseTools(queryService);
        var result = await sut.FindBridgesAsync("CodeMeridian");

        result.Should().Be("bridges");
        await queryService.Received(1).FindBridgesAsync("CodeMeridian", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CodebaseTools_FindSmellPathsAsync_ForwardsArguments()
    {
        var queryService = Substitute.For<ICodebaseQueryService>();
        queryService.FindSmellPathsAsync("CodeMeridian", 5, Arg.Any<CancellationToken>())
            .Returns("smells");

        var sut = new CodebaseTools(queryService);
        var result = await sut.FindSmellPathsAsync("CodeMeridian", 5);

        result.Should().Be("smells");
        await queryService.Received(1).FindSmellPathsAsync("CodeMeridian", 5, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CodebaseTools_KnowledgeDecayAsync_ForwardsArguments()
    {
        var queryService = Substitute.For<ICodebaseQueryService>();
        queryService.FindStaleKnowledgeAsync("CodeMeridian", 12, Arg.Any<CancellationToken>())
            .Returns("decay");

        var sut = new CodebaseTools(queryService);
        var result = await sut.KnowledgeDecayAsync("CodeMeridian", 12);

        result.Should().Be("decay");
        await queryService.Received(1).FindStaleKnowledgeAsync("CodeMeridian", 12, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CodebaseTools_ReplaceSurfaceAsync_ForwardsArguments()
    {
        var queryService = Substitute.For<ICodebaseQueryService>();
        queryService.ReplaceSurfaceAsync("Newtonsoft.Json", "System.Text.Json", "CodeMeridian", 7, Arg.Any<CancellationToken>())
            .Returns("replace");

        var sut = new CodebaseTools(queryService);
        var result = await sut.ReplaceSurfaceAsync("Newtonsoft.Json", "System.Text.Json", "CodeMeridian", 7);

        result.Should().Be("replace");
        await queryService.Received(1).ReplaceSurfaceAsync("Newtonsoft.Json", "System.Text.Json", "CodeMeridian", 7, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CodebaseTools_SuggestExtractionsAsync_ForwardsArguments()
    {
        var queryService = Substitute.For<ICodebaseQueryService>();
        queryService.SuggestExtractionsAsync("CodeMeridian", 6, Arg.Any<CancellationToken>())
            .Returns("extract");

        var sut = new CodebaseTools(queryService);
        var result = await sut.SuggestExtractionsAsync("CodeMeridian", 6);

        result.Should().Be("extract");
        await queryService.Received(1).SuggestExtractionsAsync("CodeMeridian", 6, Arg.Any<CancellationToken>());
    }

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
            0.9,
            null);

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

    [Fact]
    public async Task KnowledgeApiEndpoints_RebuildKeywordGraph_ForwardsProjectContext()
    {
        var keywordGraphService = Substitute.For<IKeywordGraphService>();
        var keywordJobService = Substitute.For<IKeywordGraphJobService>();
        keywordGraphService
            .RebuildKeywordGraphAsync("CodeMeridian", Arg.Any<CancellationToken>())
            .Returns("rebuild complete");

        var routeHandler = typeof(KnowledgeApiEndpoints)
            .GetMethod("RebuildKeywordGraph", BindingFlags.NonPublic | BindingFlags.Static);

        routeHandler.Should().NotBeNull();

        var requestType = typeof(KnowledgeApiEndpoints).Assembly.GetType("CodeMeridian.McpServer.Api.RebuildKeywordGraphRequest");
        requestType.Should().NotBeNull();

        var request = Activator.CreateInstance(requestType!, "CodeMeridian", false, null);

        var task = (Task<IResult>)routeHandler!.Invoke(null, [request, keywordGraphService, keywordJobService, CancellationToken.None])!;
        var result = await task;

        await keywordGraphService.Received(1).RebuildKeywordGraphAsync("CodeMeridian", Arg.Any<CancellationToken>());
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task KnowledgeApiEndpoints_RebuildKeywordGraph_BackgroundForwardsJobService()
    {
        var keywordGraphService = Substitute.For<IKeywordGraphService>();
        var keywordJobService = Substitute.For<IKeywordGraphJobService>();
        keywordJobService
            .StartRebuildAsync("CodeMeridian", TimeSpan.FromSeconds(900), Arg.Any<CancellationToken>())
            .Returns(new KeywordGraphJobSubmissionResult(
                true,
                "started",
                new KeywordGraphJobStatus(
                    Guid.Parse("11111111-2222-3333-4444-555555555555"),
                    "rebuild",
                    "CodeMeridian",
                    "Running",
                    DateTimeOffset.Parse("2026-06-17T10:00:00Z"),
                    DateTimeOffset.Parse("2026-06-17T10:30:00Z"),
                    null,
                    null,
                    null)));

        var routeHandler = typeof(KnowledgeApiEndpoints)
            .GetMethod("RebuildKeywordGraph", BindingFlags.NonPublic | BindingFlags.Static);

        routeHandler.Should().NotBeNull();

        var requestType = typeof(KnowledgeApiEndpoints).Assembly.GetType("CodeMeridian.McpServer.Api.RebuildKeywordGraphRequest");
        requestType.Should().NotBeNull();

        var request = Activator.CreateInstance(requestType!, "CodeMeridian", true, 900);

        var task = (Task<IResult>)routeHandler!.Invoke(null, [request, keywordGraphService, keywordJobService, CancellationToken.None])!;
        var result = await task;

        await keywordJobService.Received(1).StartRebuildAsync("CodeMeridian", TimeSpan.FromSeconds(900), Arg.Any<CancellationToken>());
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task KnowledgeApiEndpoints_ClassifyKeywords_ForwardsProjectContext()
    {
        var keywordGraphService = Substitute.For<IKeywordGraphService>();
        var keywordJobService = Substitute.For<IKeywordGraphJobService>();
        keywordGraphService
            .ClassifyKeywordsAsync("CodeMeridian", Arg.Any<CancellationToken>())
            .Returns("classification complete");

        var routeHandler = typeof(KnowledgeApiEndpoints)
            .GetMethod("ClassifyKeywords", BindingFlags.NonPublic | BindingFlags.Static);

        routeHandler.Should().NotBeNull();

        var requestType = typeof(KnowledgeApiEndpoints).Assembly.GetType("CodeMeridian.McpServer.Api.ClassifyKeywordsRequest");
        requestType.Should().NotBeNull();

        var request = Activator.CreateInstance(requestType!, "CodeMeridian", false, null);

        var task = (Task<IResult>)routeHandler!.Invoke(null, [request, keywordGraphService, keywordJobService, CancellationToken.None])!;
        var result = await task;

        await keywordGraphService.Received(1).ClassifyKeywordsAsync("CodeMeridian", Arg.Any<CancellationToken>());
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task KnowledgeApiEndpoints_GetKeywordJobStatus_ReturnsCurrentJob()
    {
        var keywordJobService = Substitute.For<IKeywordGraphJobService>();
        keywordJobService.GetStatusAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new KeywordGraphJobStatus(
                Guid.Parse("11111111-2222-3333-4444-555555555555"),
                "classify",
                "CodeMeridian",
                "Running",
                DateTimeOffset.Parse("2026-06-17T10:00:00Z"),
                DateTimeOffset.Parse("2026-06-17T10:30:00Z"),
                null,
                null,
                null));

        var routeHandler = typeof(KnowledgeApiEndpoints)
            .GetMethod("GetKeywordJobStatus", BindingFlags.NonPublic | BindingFlags.Static);

        routeHandler.Should().NotBeNull();

        var task = (Task<IResult>)routeHandler!.Invoke(null, [Guid.Parse("11111111-2222-3333-4444-555555555555"), keywordJobService, CancellationToken.None])!;
        var result = await task;

        await keywordJobService.Received(1).GetStatusAsync(Guid.Parse("11111111-2222-3333-4444-555555555555"), Arg.Any<CancellationToken>());
        result.Should().NotBeNull();
    }
}
