using System.Net.Http.Json;

namespace CodeMeridian.Sdk;

/// <summary>
/// HTTP client for the CodeMeridian knowledge ingestion API.
/// Register via <see cref="DependencyInjection.AddCodeMeridianClient"/> and inject
/// <see cref="CodeMeridianClient"/> wherever you need to talk to CodeMeridian.
/// </summary>
public sealed class CodeMeridianClient(HttpClient httpClient)
{
    public async Task<float[]?> GenerateEmbeddingAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync(
            "/api/v1/embeddings",
            new { Text = text },
            cancellationToken);

        if (!response.IsSuccessStatusCode)
            return null;

        var payload = await response.Content.ReadFromJsonAsync<EmbeddingResponse>(cancellationToken: cancellationToken);
        return payload?.Embedding;
    }

    public async Task<bool> IsEmbeddingAvailableAsync(CancellationToken cancellationToken = default)
    {
        var response = await httpClient.GetAsync("/api/v1/embeddings/availability", cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task IngestDocumentAsync(
        string content,
        string? source = null,
        string? projectContext = null,
        string? id = null,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync(
            "/api/v1/knowledge/documents",
            new { Id = id, Content = content, Source = source, ProjectContext = projectContext },
            cancellationToken);

        response.EnsureSuccessStatusCode();
    }

    public async Task IngestCodeNodeAsync(
        string id,
        string name,
        string type,
        string? namespacePath = null,
        string? filePath = null,
        int? lineNumber = null,
        int? lineCount = null,
        string? summary = null,
        string? projectContext = null,
        string? embeddingCsv = null,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync(
            "/api/v1/knowledge/nodes",
            new
            {
                Id = id,
                Name = name,
                Type = type,
                Namespace = namespacePath,
                FilePath = filePath,
                LineNumber = lineNumber,
                LineCount = lineCount,
                Summary = summary,
                ProjectContext = projectContext,
                EmbeddingCsv = embeddingCsv
            },
            cancellationToken);

        response.EnsureSuccessStatusCode();
    }

    public async Task IngestRelationshipAsync(
        string sourceId,
        string targetId,
        string relationshipType,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync(
            "/api/v1/knowledge/nodes/edges",
            new { SourceId = sourceId, TargetId = targetId, Type = relationshipType },
            cancellationToken);

        response.EnsureSuccessStatusCode();
    }

    public async Task ClearProjectKnowledgeAsync(
        string projectContext,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.DeleteAsync(
            $"/api/v1/knowledge/project/{Uri.EscapeDataString(projectContext)}",
            cancellationToken);

        response.EnsureSuccessStatusCode();
    }

    public async Task ClearProjectDiagnosticsAsync(
        string projectContext,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.DeleteAsync(
            $"/api/v1/knowledge/project/{Uri.EscapeDataString(projectContext)}/diagnostics",
            cancellationToken);

        response.EnsureSuccessStatusCode();
    }

    public async Task ClearCodeGraphAsync(CancellationToken cancellationToken = default)
    {
        var response = await httpClient.DeleteAsync(
            "/api/v1/knowledge/code-graph",
            cancellationToken);

        response.EnsureSuccessStatusCode();
    }

    private sealed record EmbeddingResponse(float[] Embedding, string ProviderName, int Dimensions);
}
