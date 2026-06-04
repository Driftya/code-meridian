namespace CodeMeridian.Core.Knowledge;

/// <summary>
/// Generates vector embeddings for code elements.
/// Optional feature — embeddings enable find_similar_nodes.
/// Implementations should be injectable and configurable via environment variables.
/// </summary>
public interface IEmbeddingProvider : IAsyncDisposable
{
    /// <summary>
    /// Generate an embedding for the given text.
    /// </summary>
    /// <param name="text">The text to embed (e.g., code summary or method signature)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Vector embedding as float array, or null if embedding is unavailable</returns>
    Task<float[]?> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// Maximum number of dimensions this provider uses.
    /// Neo4j vector index expects 1536 for OpenAI text-embedding-3-small.
    /// </summary>
    int Dimensions { get; }

    /// <summary>
    /// Human-readable provider name (e.g. "OpenAI", "Local", "Mock").
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// Whether this provider is currently available (API key set, service reachable, etc.).
    /// </summary>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);
}
