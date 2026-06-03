using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;
using Microsoft.SemanticKernel.Embeddings;

namespace CodeMeridian.Api.Endpoints;

/// <summary>
/// Endpoints for ingesting code nodes and text documents into the knowledge stores.
/// Call these from your CI/CD pipeline or codebase indexer.
/// </summary>
public static class KnowledgeEndpoints
{
    public static IEndpointRouteBuilder MapKnowledgeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/knowledge")
            .WithTags("Knowledge")
            .WithOpenApi();

        group.MapPost("/nodes", IngestNode)
            .WithName("IngestNode")
            .WithSummary("Ingest a code node into the graph");

        group.MapPost("/nodes/edges", IngestEdge)
            .WithName("IngestEdge")
            .WithSummary("Ingest a relationship between two code nodes");

        group.MapPost("/documents", IngestDocument)
            .WithName("IngestDocument")
            .WithSummary("Ingest a text document (auto-embedded) into the vector store");

        group.MapDelete("/project/{projectContext}", DeleteProject)
            .WithName("DeleteProject")
            .WithSummary("Remove all knowledge for a project context");

        return app;
    }

    private static async Task<IResult> IngestNode(
        CodeNode node,
        ICodeGraphRepository repository,
        CancellationToken cancellationToken)
    {
        await repository.UpsertNodeAsync(node, cancellationToken);
        return Results.Created($"/api/v1/knowledge/nodes/{node.Id}", node);
    }

    private static async Task<IResult> IngestEdge(
        CodeEdge edge,
        ICodeGraphRepository repository,
        CancellationToken cancellationToken)
    {
        await repository.UpsertEdgeAsync(edge, cancellationToken);
        return Results.Created("/api/v1/knowledge/nodes/edges", edge);
    }

    private static async Task<IResult> IngestDocument(
        IngestDocumentRequest request,
        IVectorRepository vectorRepository,
        ITextEmbeddingGenerationService embeddingService,
        CancellationToken cancellationToken)
    {
        var embedding = await embeddingService.GenerateEmbeddingAsync(
            request.Content,
            cancellationToken: cancellationToken);

        var document = new KnowledgeDocument
        {
            Id = request.Id ?? Guid.NewGuid().ToString("N"),
            Content = request.Content,
            Source = request.Source,
            ProjectContext = request.ProjectContext,
            Embedding = embedding.ToArray()
        };

        await vectorRepository.UpsertAsync(document, cancellationToken);
        return Results.Created($"/api/v1/knowledge/documents/{document.Id}", document.Id);
    }

    private static async Task<IResult> DeleteProject(
        string projectContext,
        ICodeGraphRepository codeGraph,
        IVectorRepository vectorStore,
        CancellationToken cancellationToken)
    {
        await Task.WhenAll(
            codeGraph.DeleteProjectAsync(projectContext, cancellationToken),
            vectorStore.DeleteProjectAsync(projectContext, cancellationToken));

        return Results.NoContent();
    }
}

public sealed record IngestDocumentRequest
{
    public string? Id { get; init; }
    public required string Content { get; init; }
    public string? Source { get; init; }
    public string? ProjectContext { get; init; }
}
