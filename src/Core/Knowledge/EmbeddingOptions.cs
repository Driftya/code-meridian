namespace CodeMeridian.Core.Knowledge;

/// <summary>
/// Configuration for the embedding provider.
/// Read from environment variables or appsettings.json.
/// Default: disabled. When enabled, uses Ollama (local) by default.
/// </summary>
public sealed class EmbeddingOptions
{
    public const string SectionName = "Embedding";

    /// <summary>
    /// Enable embedding generation during indexing.
    /// Environment variable: Embedding__Enabled
    /// Default: false (no cost, no external dependencies)
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Provider type: "Ollama" (default when enabled), "OpenAI", "Stub", "None"
    /// Environment variable: Embedding__Provider
    /// Default: "Ollama" (local, free, no API costs)
    /// </summary>
    public string Provider { get; set; } = "Ollama";

    /// <summary>
    /// Ollama base URL (for Provider="Ollama").
    /// Environment variable: Embedding__OllamaBaseUrl
    /// Default: http://localhost:11434
    /// </summary>
    public string? OllamaBaseUrl { get; set; } = "http://localhost:11434";

    /// <summary>
    /// Ollama model to use for embeddings.
    /// Recommended models: "llama2-uncased" (384 dims), "nomic-embed-text" (768 dims)
    /// Environment variable: Embedding__OllamaModel
    /// Default: "llama2-uncased"
    /// </summary>
    public string? OllamaModel { get; set; } = "llama2-uncased";

    /// <summary>
    /// OpenAI API key (for Provider="OpenAI").
    /// Environment variable: Embedding__OpenAiApiKey or OPENAI_API_KEY
    /// </summary>
    public string? OpenAiApiKey { get; set; }

    /// <summary>
    /// OpenAI model to use for embeddings.
    /// Recommended: "text-embedding-3-small" (1536 dimensions, $0.02/M tokens)
    /// Environment variable: Embedding__OpenAiModel
    /// </summary>
    public string OpenAiModel { get; set; } = "text-embedding-3-small";

    /// <summary>
    /// Max batch size for embedding requests.
    /// Ollama: up to 50-100 (local, no strict limits)
    /// OpenAI: up to 2048 (cloud API)
    /// Environment variable: Embedding__BatchSize
    /// </summary>
    public int BatchSize { get; set; } = 50;

    /// <summary>
    /// Warning message shown when embeddings are disabled or unavailable.
    /// </summary>
    public string NoEmbeddingsWarning => 
        "⚠️  Embeddings are disabled. " +
        "Set Embedding__Enabled=true to enable find_similar_nodes. " +
        "Default provider: Ollama (local, free). For cloud embeddings, use Embedding__Provider=OpenAI. " +
        "See https://github.com/driftya/CodeMeridian/docs/embeddings.md for setup.";
}
