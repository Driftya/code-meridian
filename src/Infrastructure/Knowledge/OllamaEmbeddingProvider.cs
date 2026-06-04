using System.Net.Http.Json;
using CodeMeridian.Core.Knowledge;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CodeMeridian.Infrastructure.Knowledge;

/// <summary>
/// Ollama local embedding provider.
/// Uses a local Ollama instance to generate embeddings without API costs or external dependencies.
/// Models: llama2-uncased (384 dims), nomic-embed-text (768 dims), etc.
/// </summary>
public sealed class OllamaEmbeddingProvider : IEmbeddingProvider, IAsyncDisposable
{
    private readonly HttpClient _httpClient;
    private readonly EmbeddingOptions _options;
    private readonly ILogger<OllamaEmbeddingProvider> _logger;
    private bool _available = false;

    public int Dimensions => 384; // llama2-uncased default
    public string ProviderName => "Ollama";

    public OllamaEmbeddingProvider(
        HttpClient httpClient,
        IOptions<EmbeddingOptions> options,
        ILogger<OllamaEmbeddingProvider> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<float[]?> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        try
        {
            var baseUrl = _options.OllamaBaseUrl ?? "http://localhost:11434";
            var model = _options.OllamaModel ?? "llama2-uncased";

            var request = new
            {
                model,
                prompt = text
            };

            var response = await _httpClient.PostAsJsonAsync(
                $"{baseUrl}/api/embeddings",
                request,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Ollama embedding request failed with {StatusCode}: {Error}",
                    response.StatusCode, errorContent);
                return null;
            }

            var result = await response.Content.ReadAsAsync<OllamaEmbeddingResponse>(cancellationToken);
            return result?.Embedding;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating embedding via Ollama");
            return null;
        }
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        if (_available)
            return true;

        try
        {
            var baseUrl = _options.OllamaBaseUrl ?? "http://localhost:11434";
            
            // Health check: try to list available models
            var response = await _httpClient.GetAsync($"{baseUrl}/api/tags", cancellationToken);
            _available = response.IsSuccessStatusCode;
            
            if (!_available)
            {
                _logger.LogWarning(
                    "Ollama server not available at {BaseUrl}. " +
                    "Ensure Ollama is running: ollama serve",
                    baseUrl);
            }

            return _available;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ollama embedding provider availability check failed. Ensure Ollama is running.");
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _httpClient.Dispose();
        await ValueTask.CompletedTask;
    }

    private sealed record OllamaEmbeddingResponse(
        float[] Embedding
    );
}
