using System.Net.Http.Json;

namespace CodeMeridian.Sdk;

/// <summary>
/// HTTP client for the CodeMeridian knowledge ingestion API.
/// Register via <see cref="DependencyInjection.AddCodeMeridianClient"/> and inject
/// <see cref="CodeMeridianClient"/> wherever you need to talk to CodeMeridian.
/// </summary>
public sealed class CodeMeridianClient(HttpClient httpClient)
{
    public async Task<DoctorStatusResponse?> GetDoctorStatusAsync(
        string? projectContext = null,
        CancellationToken cancellationToken = default)
    {
        var path = "/api/v1/status/doctor";
        if (!string.IsNullOrWhiteSpace(projectContext))
            path += $"?projectContext={Uri.EscapeDataString(projectContext)}";

        var response = await httpClient.GetAsync(path, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;

        return await response.Content.ReadFromJsonAsync<DoctorStatusResponse>(cancellationToken: cancellationToken);
    }

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
        string? relatedNodeIdsCsv = null,
        CancellationToken cancellationToken = default)
    {
        var response = await httpClient.PostAsJsonAsync(
            "/api/v1/knowledge/documents",
            new { Id = id, Content = content, Source = source, ProjectContext = projectContext, RelatedNodeIdsCsv = relatedNodeIdsCsv },
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
        string? sourceSnippet = null,
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
                SourceSnippet = sourceSnippet,
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

    public async Task DeleteProjectFileAsync(
        string projectContext,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        var normalizedPath = filePath.Replace('\\', '/');
        var response = await httpClient.DeleteAsync(
            $"/api/v1/knowledge/project/{Uri.EscapeDataString(projectContext)}/files/{Uri.EscapeDataString(normalizedPath)}",
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

public sealed record DoctorStatusResponse(
    string? ProjectContext,
    bool Neo4jReachable,
    long IndexedNodes,
    long CallEdges,
    long DocumentsIndexed,
    long DiagnosticsIndexed,
    string GraphDrift,
    string GraphDriftReport,
    bool EmbeddingsEnabled,
    string EmbeddingProvider,
    int EmbeddingDimensions,
    string? Error);
