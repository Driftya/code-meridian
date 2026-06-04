namespace CodeMeridian.Core.Knowledge;

/// <summary>
/// Null object pattern: no-op embedding provider.
/// Used when embeddings are disabled or unavailable.
/// </summary>
public sealed class NoOpEmbeddingProvider : IEmbeddingProvider
{
    public int Dimensions => 0;
    public string ProviderName => "None";

    public Task<float[]?> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
        => Task.FromResult<float[]?>(null);

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(false);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>
/// Stub provider for testing. Always returns a small random embedding.
/// </summary>
public sealed class StubEmbeddingProvider : IEmbeddingProvider
{
    private readonly Random _random = new(42); // Deterministic for tests

    public int Dimensions => 4;
    public string ProviderName => "Stub";

    public Task<float[]?> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Task.FromResult<float[]?>(null);

        var embedding = new float[Dimensions];
        lock (_random)
        {
            for (int i = 0; i < Dimensions; i++)
                embedding[i] = (float)_random.NextDouble() - 0.5f;
        }

        return Task.FromResult<float[]?>(embedding);
    }

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
