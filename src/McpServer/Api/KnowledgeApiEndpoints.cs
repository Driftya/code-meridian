using System.Globalization;
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
        group.MapDelete("/project/{projectContext}/diagnostics", DeleteDiagnostics);
        group.MapDelete("/project/{projectContext}/files/{**filePath}", DeleteProjectFile);
        group.MapDelete("/code-graph", DeleteCodeGraph);
        group.MapDelete("/project/{projectContext}", DeleteProject);

        return app;
    }

    private static async Task<IResult> IngestNode(
        IngestNodeRequest req,
        ICodeGraphRepository repo,
        IEmbeddingProvider embeddingProvider,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        if (!Enum.TryParse<CodeNodeType>(req.Type, ignoreCase: true, out var nodeType))
            return Results.BadRequest($"Unknown type '{req.Type}'");

        float[]? embedding;
        try
        {
            embedding = ParseEmbedding(req.EmbeddingCsv);
        }
        catch (FormatException)
        {
            return Results.BadRequest("Invalid embeddingCsv format. Expected comma-separated floats.");
        }

        if (embedding is null && IsEmbeddableType(nodeType) && await embeddingProvider.IsAvailableAsync(ct))
        {
            embedding = await embeddingProvider.GenerateEmbeddingAsync(GenerateEmbeddingText(req), ct);
        }

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
            SourceSnippet = req.SourceSnippet,
            SourceHash = req.SourceHash,
            ProjectContext = req.ProjectContext,
            Embedding = embedding
        }, ct);

        if (embedding is { Length: > 0 })
        {
            var logger = loggerFactory.CreateLogger("CodeMeridian.McpServer.Api.KnowledgeIngest");
            logger.LogInformation(
                "Stored code node embedding for {NodeId} with {Dimensions} dimensions.",
                req.Id,
                embedding.Length);
        }

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
            ProjectContext = req.ProjectContext,
            Metadata = ParseMetadata(req.RelatedNodeIdsCsv, req.RelatedDocumentIdsCsv)
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

    private static async Task<IResult> DeleteProjectFile(
        string projectContext,
        string filePath,
        ICodeGraphRepository codeGraph,
        IVectorRepository vectorStore,
        CancellationToken ct)
    {
        var normalizedFilePath = Uri.UnescapeDataString(filePath).Replace('\\', '/');
        await Task.WhenAll(
            codeGraph.DeleteFileAsync(projectContext, normalizedFilePath, ct),
            vectorStore.DeleteSourceAsync(projectContext, normalizedFilePath, ct));

        return Results.NoContent();
    }

    private static async Task<IResult> DeleteCodeGraph(
        ICodeGraphRepository codeGraph,
        CancellationToken ct)
    {
        await codeGraph.DeleteAllAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteDiagnostics(
        string projectContext,
        ICodeGraphRepository codeGraph,
        CancellationToken ct)
    {
        await codeGraph.DeleteDiagnosticsAsync(projectContext, ct);
        return Results.NoContent();
    }

    private static float[]? ParseEmbedding(string? embeddingCsv)
    {
        if (string.IsNullOrWhiteSpace(embeddingCsv))
            return null;

        var values = embeddingCsv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(value => float.Parse(value, CultureInfo.InvariantCulture))
            .ToArray();

        return values.Length > 0 ? values : null;
    }

    private static bool IsEmbeddableType(CodeNodeType nodeType) =>
        nodeType is CodeNodeType.Class or CodeNodeType.Interface or CodeNodeType.Method or CodeNodeType.Enum or CodeNodeType.Diagnostic;

    private static string GenerateEmbeddingText(IngestNodeRequest req) =>
        string.Join(" ",
            new[]
            {
                $"{req.Type} {req.Name}",
                req.Namespace is not null ? $"in {req.Namespace}" : null,
                req.Summary is not null ? $"- {req.Summary}" : null,
                NormalizeEmbeddingText(req.SourceSnippet)
            }.Where(part => !string.IsNullOrWhiteSpace(part)));

    private static string? NormalizeEmbeddingText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = value
            .ReplaceLineEndings(" ")
            .Trim();

        return normalized.Length > 1_500
            ? normalized[..1_500]
            : normalized;
    }

    private static Dictionary<string, string> ParseMetadata(string? relatedNodeIdsCsv, string? relatedDocumentIdsCsv)
    {
        if (string.IsNullOrWhiteSpace(relatedNodeIdsCsv))
        {
            var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(relatedDocumentIdsCsv))
                metadata["relatedDocumentIds"] = relatedDocumentIdsCsv;
            return metadata;
        }

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["relatedNodeIds"] = relatedNodeIdsCsv
        };

        if (!string.IsNullOrWhiteSpace(relatedDocumentIdsCsv))
            result["relatedDocumentIds"] = relatedDocumentIdsCsv;

        return result;
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

internal sealed record IngestNodeRequest(
    string Id,
    string Name,
    string Type,
    string? Namespace = null,
    string? FilePath = null,
    int? LineNumber = null,
    int? LineCount = null,
    string? Summary = null,
    string? SourceSnippet = null,
    string? SourceHash = null,
    string? ProjectContext = null,
    string? EmbeddingCsv = null);

internal sealed record IngestEdgeRequest(
    string SourceId,
    string TargetId,
    string Type);

internal sealed record IngestDocumentRequest(
    string Content,
    string? Id = null,
    string? Source = null,
    string? ProjectContext = null,
    string? RelatedNodeIdsCsv = null,
    string? RelatedDocumentIdsCsv = null);

internal sealed record EmbeddingRequest(string Text);

internal sealed record EmbeddingResponse(
    float[] Embedding,
    string ProviderName,
    int Dimensions);

internal sealed record EmbeddingAvailabilityResponse(
    string ProviderName,
    int Dimensions);
