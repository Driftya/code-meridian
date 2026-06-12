namespace CodeMeridian.DocumentIndexer.Pipeline;

internal static class DocumentChunkReferenceBuilder
{
    public static string BuildChunkDocumentId(string projectContext, string relPath, int chunkCount, int chunkIndex) =>
        chunkCount == 1
            ? $"{projectContext}::doc::{relPath}"
            : $"{projectContext}::doc::{relPath}::part{chunkIndex + 1}";

    public static IReadOnlyList<string> BuildAdjacentChunkIds(string projectContext, string relPath, int chunkCount, int chunkIndex)
    {
        var related = new List<string>(2);
        if (chunkCount <= 1)
            return related;

        if (chunkIndex > 0)
            related.Add(BuildChunkDocumentId(projectContext, relPath, chunkCount, chunkIndex - 1));

        if (chunkIndex + 1 < chunkCount)
            related.Add(BuildChunkDocumentId(projectContext, relPath, chunkCount, chunkIndex + 1));

        return related;
    }

    public static string? BuildRelatedDocumentIdsCsv(IEnumerable<string> relatedDocuments, IEnumerable<string> relatedChunkIds)
    {
        var references = relatedDocuments.Concat(relatedChunkIds).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return references.Length == 0 ? null : string.Join(",", references);
    }
}
