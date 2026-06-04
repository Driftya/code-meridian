using System.Net.Http.Json;
using CodeMeridian.Core.Knowledge;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CodeMeridian.Infrastructure.Knowledge;

/// <summary>
/// OpenAI text-embedding-3-small provider.
/// Uses the OpenAI API to generate vector embeddings for code nodes.
/// Model: text-embedding-3-small (1536 dimensions, $0.02/M tokens)
/// </summary>
public sealed class OpenAiEmbeddingProvider : IEmbeddingProvider, IAsyncDisposable
{
    private readonly HttpClient _httpClient;
    private readonly EmbeddingOptions _options;
    private readonly ILogger<OpenAiEmbeddingProvider> _logger;
    private bool _available = false;

    public int Dimensions => 1536;
    public string ProviderName => "OpenAI";

    public OpenAiEmbeddingProvider(
        HttpClient httpClient,
        IOptions<EmbeddingOptions> options,
        ILogger<OpenAiEmbeddingProvider> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        if (!string.IsNullOrWhiteSpace(_options.OpenAiApiKey))
        {
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_options.OpenAiApiKey}");
        }
    }

    public async Task<float[]?> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        try
        {
            var request = new
            {
                input = text,
                model = _options.OpenAiModel,
                encoding_format = "float"
            };

            var response = await _httpClient.PostAsJsonAsync(
                "https://api.openai.com/v1/embeddings",
                request,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "OpenAI embedding request failed with {StatusCode}: {Error}",
                    response.StatusCode, errorContent);
                return null;
            }

            var result = await response.Content.ReadAsAsync<OpenAiEmbeddingResponse>(cancellationToken);
            return result?.Data?.FirstOrDefault()?.Embedding;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating embedding via OpenAI");
            return null;
        }
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        if (_available)
            return true;

        if (string.IsNullOrWhiteSpace(_options.OpenAiApiKey))
        {
            _logger.LogWarning("OpenAI embedding provider requires Embedding__OpenAiApiKey to be set");
            return false;
        }

        try
        {
            // Test with a minimal request
            var embedding = await GenerateEmbeddingAsync("test", cancellationToken);
            _available = embedding is not null;
            return _available;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OpenAI embedding provider availability check failed");
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _httpClient.Dispose();
        await ValueTask.CompletedTask;
    }

    private sealed record OpenAiEmbeddingResponse(
        List<EmbeddingData> Data
    );

    private sealed record EmbeddingData(
        float[] Embedding,
        int Index
    );
}
