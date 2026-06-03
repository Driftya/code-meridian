namespace CodeMeridian.Core.Knowledge;

public interface IVectorRepository
{
    Task UpsertAsync(KnowledgeDocument document, CancellationToken cancellationToken = default);

    /// <summary>Semantic similarity search — requires embeddings to be stored on documents.</summary>
    Task<IReadOnlyList<KnowledgeDocument>> SearchAsync(float[] queryEmbedding, string? projectContext = null, int topK = 10, CancellationToken cancellationToken = default);

    /// <summary>Full-text search — works without any embedding model.</summary>
    Task<IReadOnlyList<KnowledgeDocument>> SearchByTextAsync(string query, string? projectContext = null, int topK = 10, CancellationToken cancellationToken = default);

    Task DeleteAsync(string id, CancellationToken cancellationToken = default);
    Task DeleteProjectAsync(string projectContext, CancellationToken cancellationToken = default);
}
