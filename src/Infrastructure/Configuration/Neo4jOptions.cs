namespace CodeMeridian.Infrastructure.Configuration;

public sealed class Neo4jOptions
{
    public const string SectionName = "Neo4j";

    public required string Uri { get; init; }
    public required string Username { get; init; }
    public required string Password { get; init; }
    public int MaxConnectionPoolSize { get; init; } = 100;
    public int ConnectionTimeoutSeconds { get; init; } = 30;

    /// <summary>Dimensions of the embedding model (1536 for text-embedding-3-small).</summary>
    public int EmbeddingDimensions { get; init; } = 1536;
}
