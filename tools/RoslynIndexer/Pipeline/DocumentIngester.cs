using CodeMeridian.Sdk;
using Microsoft.Extensions.Logging;

namespace CodeMeridian.RoslynIndexer.Pipeline;

/// <summary>
/// Ingests markdown, text, and JSON files into CodeMeridian's document store
/// so Copilot can search them via the search_documentation MCP tool.
/// </summary>
public sealed class DocumentIngester(CodeMeridianClient client, ILogger<DocumentIngester> logger)
{
    // Files larger than this are split into chunks to stay within Neo4j node limits
    private const int MaxChunkChars = 4_000;

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

                var chunks = SplitIntoChunks(content, MaxChunkChars);

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

    private static List<string> SplitIntoChunks(string text, int maxChars)
    {
        if (text.Length <= maxChars) return [text];

        var chunks = new List<string>();
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');

        // Split on paragraph boundaries (blank lines) first.
        // Normalize line endings so CRLF docs split the same way as LF docs.
        var paragraphs = normalized.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var current = new System.Text.StringBuilder();

        foreach (var paragraph in paragraphs)
        {
            if (paragraph.Length > maxChars)
            {
                if (current.Length > 0)
                {
                    chunks.Add(current.ToString().Trim());
                    current.Clear();
                }

                for (var offset = 0; offset < paragraph.Length; offset += maxChars)
                {
                    var slice = paragraph[offset..Math.Min(offset + maxChars, paragraph.Length)].Trim();
                    if (slice.Length > 0)
                        chunks.Add(slice);
                }

                continue;
            }

            var separatorLength = current.Length > 0 ? 2 : 0;
            if (current.Length + separatorLength + paragraph.Length > maxChars && current.Length > 0)
            {
                chunks.Add(current.ToString().Trim());
                current.Clear();
            }

            if (current.Length > 0)
                current.Append("\n\n");

            current.Append(paragraph);
        }

        if (current.Length > 0)
            chunks.Add(current.ToString().Trim());

        return chunks.Count > 0 ? chunks : [normalized[..maxChars]];
    }
}
