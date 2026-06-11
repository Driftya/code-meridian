using CodeMeridian.Sdk;
using CodeMeridian.Tooling.Documents;
using Microsoft.Extensions.Logging;

namespace CodeMeridian.RoslynIndexer.Pipeline;

/// <summary>
/// Ingests markdown, text, and JSON files into CodeMeridian's document store
/// so Copilot can search them via the search_documentation MCP tool.
/// </summary>
public sealed class DocumentIngester(CodeMeridianClient client, ILogger<DocumentIngester> logger)
{
    public async Task<DocumentStats> IngestAsync(
        FileInfo[] files,
        string projectContext,
        string rootPath,
        CancellationToken cancellationToken = default)
    {
        var count = 0;

        foreach (var file in files)
        {
            try
            {
                var relPath = Path.GetRelativePath(rootPath, file.FullName).Replace('\\', '/');
                var content = await File.ReadAllTextAsync(file.FullName, cancellationToken);

                if (string.IsNullOrWhiteSpace(content)) continue;

                var chunks = DocumentTextSplitter.SplitIntoChunks(content);

                logger.LogInformation(
                    "  {File} ? {Chunks} chunk(s)", relPath, chunks.Count);

                for (var i = 0; i < chunks.Count; i++)
                {
                    var id = chunks.Count == 1
                        ? $"{projectContext}::doc::{relPath}"
                        : $"{projectContext}::doc::{relPath}::part{i + 1}";

                    await client.IngestDocumentAsync(
                        content: chunks[i],
                        source: relPath,
                        projectContext: projectContext,
                        id: id,
                        cancellationToken: cancellationToken);

                    count++;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning("Skipped {File}: {Error}", file.Name, ex.Message);
            }
        }

        return new DocumentStats(count);
    }
}
