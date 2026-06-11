using CodeMeridian.Sdk;
using CodeMeridian.Tooling.Documents;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace CodeMeridian.DocumentIndexer.Pipeline;

public sealed partial class DocumentIndexerPipeline(CodeMeridianClient client, ILogger<DocumentIndexerPipeline> logger)
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

                if (string.IsNullOrWhiteSpace(content))
                    continue;

                var chunks = DocumentTextSplitter.SplitIntoChunks(content);
                var relatedDocuments = ExtractDocumentReferences(content, relPath);
                var relatedNodes = ExtractCodeFileReferences(content, rootPath, projectContext);
                logger.LogInformation("  {File} -> {Chunks} chunk(s)", relPath, chunks.Count);

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
                        relatedNodeIdsCsv: relatedNodes.Count > 0 ? string.Join(",", relatedNodes) : null,
                        relatedDocumentIdsCsv: relatedDocuments.Count > 0 ? string.Join(",", relatedDocuments) : null,
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

    private static List<string> ExtractDocumentReferences(string content, string sourcePath)
    {
        var references = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in MarkdownLinkRegex().Matches(content))
        {
            var target = match.Groups["target"].Value.Trim();
            if (string.IsNullOrWhiteSpace(target))
                continue;

            if (target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || target.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                continue;

            var normalized = target.TrimStart('.', '/').Replace('\\', '/');
            if (normalized.Length == 0)
                continue;

            if (normalized.StartsWith('#'))
                continue;

            references.Add(normalized);
        }

        return references.ToList();
    }

    private static List<string> ExtractCodeFileReferences(string content, string rootPath, string projectContext)
    {
        var references = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in MarkdownLinkRegex().Matches(content))
        {
            var target = match.Groups["target"].Value.Trim();
            if (string.IsNullOrWhiteSpace(target))
                continue;

            if (target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || target.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                continue;

            var normalized = NormalizeLinkTarget(target);
            if (normalized is null || !IsCodeFile(normalized))
                continue;

            AddCodeFileNodeIdCandidates(references, projectContext, normalized);
        }

        foreach (Match match in InlineCodePathRegex().Matches(content))
        {
            var normalized = NormalizeLinkTarget(match.Value);
            if (normalized is null || !IsCodeFile(normalized))
                continue;

            AddCodeFileNodeIdCandidates(references, projectContext, normalized);
        }

        return references.ToList();
    }

    private static void AddCodeFileNodeIdCandidates(HashSet<string> references, string projectContext, string relativePath)
    {
        references.Add($"{projectContext}:File:{relativePath}");
        references.Add($"{projectContext}::File::{relativePath}");
    }

    private static string? NormalizeLinkTarget(string target)
    {
        var normalized = target.Split('#', '?')[0].Trim();
        normalized = normalized.TrimStart('.', '/').Replace('\\', '/');
        return normalized.Length == 0 ? null : normalized;
    }

    private static bool IsCodeFile(string path) =>
        path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".jsx", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"\[[^\]]+\]\((?<target>[^)]+)\)", RegexOptions.Compiled)]
    private static partial Regex MarkdownLinkRegex();

    [GeneratedRegex(@"\b(?:[A-Za-z]:)?(?:[./\\][^\s`'""<>|]+)+\.(?:cs|ts|tsx|js|jsx)\b", RegexOptions.Compiled)]
    private static partial Regex InlineCodePathRegex();
}

public sealed record DocumentStats(int DocumentsIndexed);
