using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;

namespace CodeMeridian.McpServer.Api;

/// <summary>
/// Minimal REST API used by the CodeMeridian Indexer (and any other tooling)
/// to push code nodes, edges, and documents into Neo4j.
/// GitHub Copilot uses the MCP tools instead — these endpoints serve automation scripts.
/// </summary>
public static class KnowledgeApiEndpoints
{
    public static IEndpointRouteBuilder MapKnowledgeApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/knowledge").WithTags("Knowledge");

        group.MapPost("/nodes", IngestNode);
        group.MapPost("/nodes/edges", IngestEdge);
        group.MapPost("/documents", IngestDocument);
        group.MapDelete("/code-graph", DeleteCodeGraph);
        group.MapDelete("/project/{projectContext}", DeleteProject);

        return app;
    }

    private static async Task<IResult> IngestNode(
        IngestNodeRequest req,
        ICodeGraphRepository repo,
        CancellationToken ct)
    {
        if (!Enum.TryParse<CodeNodeType>(req.Type, ignoreCase: true, out var nodeType))
            return Results.BadRequest($"Unknown type '{req.Type}'");

        await repo.UpsertNodeAsync(new CodeNode
        {
            Id = req.Id,
            Name = req.Name,
            Type = nodeType,
            Namespace = req.Namespace,
            FilePath = req.FilePath,
            LineNumber = req.LineNumber,
            LineCount = req.LineCount,
            Summary = req.Summary,
            ProjectContext = req.ProjectContext
        }, ct);

        return Results.Created($"/api/v1/knowledge/nodes/{Uri.EscapeDataString(req.Id)}", req.Id);
    }

    private static async Task<IResult> IngestEdge(
        IngestEdgeRequest req,
        ICodeGraphRepository repo,
        CancellationToken ct)
    {
        if (!Enum.TryParse<CodeEdgeType>(req.Type, ignoreCase: true, out var edgeType))
            return Results.BadRequest($"Unknown relationship type '{req.Type}'");

        await repo.UpsertEdgeAsync(new CodeEdge
        {
            SourceId = req.SourceId,
            TargetId = req.TargetId,
            Type = edgeType
        }, ct);

        return Results.Created("/api/v1/knowledge/nodes/edges", null);
    }

    private static async Task<IResult> IngestDocument(
        IngestDocumentRequest req,
        IVectorRepository vectorRepo,
        CancellationToken ct)
    {
        await vectorRepo.UpsertAsync(new KnowledgeDocument
        {
            Id = req.Id ?? Guid.NewGuid().ToString("N"),
            Content = req.Content,
            Source = req.Source,
            ProjectContext = req.ProjectContext
        }, ct);

        return Results.Created("/api/v1/knowledge/documents", null);
    }

    private static async Task<IResult> DeleteProject(
        string projectContext,
        ICodeGraphRepository codeGraph,
        IVectorRepository vectorStore,
        CancellationToken ct)
    {
        await Task.WhenAll(
            codeGraph.DeleteProjectAsync(projectContext, ct),
            vectorStore.DeleteProjectAsync(projectContext, ct));

        return Results.NoContent();
    }

    private static async Task<IResult> DeleteCodeGraph(
        ICodeGraphRepository codeGraph,
        CancellationToken ct)
    {
        await codeGraph.DeleteAllAsync(ct);
        return Results.NoContent();
    }
}

public static class EmbeddingApiEndpoints
{
    public static IEndpointRouteBuilder MapEmbeddingApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/embeddings").WithTags("Embeddings");

        group.MapGet("/availability", GetAvailability);
        group.MapPost(string.Empty, GenerateEmbedding);

        return app;
    }

    private static async Task<IResult> GetAvailability(
        IEmbeddingProvider embeddingProvider,
        CancellationToken ct)
    {
        if (!await embeddingProvider.IsAvailableAsync(ct))
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);

        return Results.Ok(new EmbeddingAvailabilityResponse(
            embeddingProvider.ProviderName,
            embeddingProvider.Dimensions));
    }

    private static async Task<IResult> GenerateEmbedding(
        EmbeddingRequest req,
        IEmbeddingProvider embeddingProvider,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Text))
            return Results.BadRequest("Text is required.");

        if (!await embeddingProvider.IsAvailableAsync(ct))
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);

        var embedding = await embeddingProvider.GenerateEmbeddingAsync(req.Text, ct);
        if (embedding is null || embedding.Length == 0)
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);

        return Results.Ok(new EmbeddingResponse(
            embedding,
            embeddingProvider.ProviderName,
            embeddingProvider.Dimensions));
    }
}

// ── Request DTOs ──────────────────────────────────────────────────────────────

internal sealed record IngestNodeRequest(
    string Id,
    string Name,
    string Type,
    string? Namespace = null,
    string? FilePath = null,
    int? LineNumber = null,
    int? LineCount = null,
    string? Summary = null,
    string? ProjectContext = null);

internal sealed record IngestEdgeRequest(
    string SourceId,
    string TargetId,
    string Type);

internal sealed record IngestDocumentRequest(
    string Content,
    string? Id = null,
    string? Source = null,
    string? ProjectContext = null);

internal sealed record EmbeddingRequest(string Text);

internal sealed record EmbeddingResponse(
    float[] Embedding,
    string ProviderName,
    int Dimensions);

internal sealed record EmbeddingAvailabilityResponse(
    string ProviderName,
    int Dimensions);
