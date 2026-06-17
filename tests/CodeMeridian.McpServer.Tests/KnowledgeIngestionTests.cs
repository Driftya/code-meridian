using System.Reflection;
using CodeMeridian.Application.Services;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;
using CodeMeridian.McpServer.Api;
using CodeMeridian.McpServer.Keywording;
using CodeMeridian.McpServer.Tools;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
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
    public async Task CodebaseTools_ArchitectureDriftHistoryAsync_ForwardsArguments()
    {
        var queryService = Substitute.For<ICodebaseQueryService>();
        queryService.FindArchitectureErosionTimelineAsync("CodeMeridian", 14, Arg.Any<CancellationToken>())
            .Returns("timeline");

        var sut = new CodebaseTools(queryService);
        var result = await sut.FindArchitectureErosionTimelineAsync("CodeMeridian", 14);

        result.Should().Be("timeline");
        await queryService.Received(1).FindArchitectureErosionTimelineAsync("CodeMeridian", 14, Arg.Any<CancellationToken>());
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
    public async Task CodebaseTools_SuggestResponsibilitySlicesAsync_ForwardsArguments()
    {
        var queryService = Substitute.For<ICodebaseQueryService>();
        queryService.SuggestResponsibilitySlicesAsync("CodebaseQueryService", "CodeMeridian", 4, true, false, true, false, Arg.Any<CancellationToken>())
            .Returns("slices");

        var sut = new CodebaseTools(queryService);
        var result = await sut.SuggestResponsibilitySlicesAsync(
            "CodebaseQueryService",
            "CodeMeridian",
            4,
            includeNamespacePlan: true,
            includeTestPlan: false,
            includeMigrationSteps: true,
            includeSourceSnippets: false);

        result.Should().Be("slices");
        await queryService.Received(1).SuggestResponsibilitySlicesAsync("CodebaseQueryService", "CodeMeridian", 4, true, false, true, false, Arg.Any<CancellationToken>());
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
    public async Task KnowledgeApiEndpoints_IngestNode_QueuesKeywordRefresh()
    {
        var repo = Substitute.For<ICodeGraphRepository>();
        var embeddings = Substitute.For<IEmbeddingProvider>();
        embeddings.IsAvailableAsync(Arg.Any<CancellationToken>()).Returns(false);
        var queue = Substitute.For<IKeywordRefreshQueue>();
        var routeHandler = typeof(KnowledgeApiEndpoints)
            .GetMethod("IngestNode", BindingFlags.NonPublic | BindingFlags.Static);

        routeHandler.Should().NotBeNull();

        var requestType = typeof(KnowledgeApiEndpoints).Assembly.GetType("CodeMeridian.McpServer.Api.IngestNodeRequest");
        requestType.Should().NotBeNull();

        var request = Activator.CreateInstance(
            requestType!,
            "node-1",
            "OrderService",
            "Class",
            "Shop",
            "src/OrderService.cs",
            12,
            40,
            "Handles orders",
            null,
            "abc",
            "Source",
            "CodeMeridian",
            null,
            null);

        var task = (Task<IResult>)routeHandler!.Invoke(null, [request, repo, embeddings, queue, Substitute.For<ILoggerFactory>(), CancellationToken.None])!;
        await task;

        await queue.Received(1).QueueAsync(
            Arg.Is<KeywordRefreshWorkItem>(item =>
                item.SourceNodeId == "node-1" &&
                item.ProjectContext == "CodeMeridian"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task KnowledgeApiEndpoints_IngestDocument_QueuesKeywordRefreshWithGeneratedId()
    {
        var vector = Substitute.For<IVectorRepository>();
        var queue = Substitute.For<IKeywordRefreshQueue>();
        KnowledgeDocument? captured = null;
        KeywordRefreshWorkItem? queued = null;

        vector
            .UpsertAsync(Arg.Do<KnowledgeDocument>(doc => captured = doc), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        queue
            .QueueAsync(Arg.Do<KeywordRefreshWorkItem>(item => queued = item), Arg.Any<CancellationToken>())
            .Returns(ValueTask.CompletedTask);

        var routeHandler = typeof(KnowledgeApiEndpoints)
            .GetMethod("IngestDocument", BindingFlags.NonPublic | BindingFlags.Static);

        routeHandler.Should().NotBeNull();

        var requestType = typeof(KnowledgeApiEndpoints).Assembly.GetType("CodeMeridian.McpServer.Api.IngestDocumentRequest");
        requestType.Should().NotBeNull();

        var request = Activator.CreateInstance(
            requestType!,
            "Notes",
            null,
            "docs/notes.md",
            "CodeMeridian",
            null,
            null);

        var task = (Task<IResult>)routeHandler!.Invoke(null, [request, vector, queue, CancellationToken.None])!;
        await task;

        captured.Should().NotBeNull();
        queued.Should().NotBeNull();
        queued!.SourceNodeId.Should().Be(captured!.Id);
        queued.ProjectContext.Should().Be("CodeMeridian");
    }

    [Fact]
    public async Task KnowledgeApiEndpoints_RebuildKeywordGraph_AlwaysQueuesBackgroundJob()
    {
        var keywordJobService = Substitute.For<IKeywordGraphJobService>();
        keywordJobService
            .StartRebuildAsync("CodeMeridian", null, Arg.Any<CancellationToken>())
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

        var request = Activator.CreateInstance(requestType!, "CodeMeridian", null);

        var task = (Task<IResult>)routeHandler!.Invoke(null, [request, keywordJobService, CancellationToken.None])!;
        var result = await task;

        await keywordJobService.Received(1).StartRebuildAsync("CodeMeridian", null, Arg.Any<CancellationToken>());
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task KnowledgeApiEndpoints_RebuildKeywordGraph_UsesLeaseTtl()
    {
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

        var request = Activator.CreateInstance(requestType!, "CodeMeridian", 900);

        var task = (Task<IResult>)routeHandler!.Invoke(null, [request, keywordJobService, CancellationToken.None])!;
        var result = await task;

        await keywordJobService.Received(1).StartRebuildAsync("CodeMeridian", TimeSpan.FromSeconds(900), Arg.Any<CancellationToken>());
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task KnowledgeApiEndpoints_ClassifyKeywords_AlwaysQueuesBackgroundJob()
    {
        var keywordJobService = Substitute.For<IKeywordGraphJobService>();
        keywordJobService
            .StartClassifyAsync("CodeMeridian", null, Arg.Any<CancellationToken>())
            .Returns(new KeywordGraphJobSubmissionResult(
                true,
                "started",
                new KeywordGraphJobStatus(
                    Guid.Parse("11111111-2222-3333-4444-555555555555"),
                    "classify",
                    "CodeMeridian",
                    "Running",
                    DateTimeOffset.Parse("2026-06-17T10:00:00Z"),
                    DateTimeOffset.Parse("2026-06-17T10:30:00Z"),
                    null,
                    null,
                    null)));

        var routeHandler = typeof(KnowledgeApiEndpoints)
            .GetMethod("ClassifyKeywords", BindingFlags.NonPublic | BindingFlags.Static);

        routeHandler.Should().NotBeNull();

        var requestType = typeof(KnowledgeApiEndpoints).Assembly.GetType("CodeMeridian.McpServer.Api.ClassifyKeywordsRequest");
        requestType.Should().NotBeNull();

        var request = Activator.CreateInstance(requestType!, "CodeMeridian", null);

        var task = (Task<IResult>)routeHandler!.Invoke(null, [request, keywordJobService, CancellationToken.None])!;
        var result = await task;

        await keywordJobService.Received(1).StartClassifyAsync("CodeMeridian", null, Arg.Any<CancellationToken>());
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
