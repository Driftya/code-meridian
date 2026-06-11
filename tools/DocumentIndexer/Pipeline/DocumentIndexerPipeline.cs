using CodeMeridian.Sdk;
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
        var mcpToolFiles = BuildMcpToolFileMap(rootPath, projectContext);

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
                var relatedNodes = ExtractCodeFileReferences(content, projectContext, relPath);
                relatedNodes.AddRange(ExtractRouteReferences(content, projectContext));
                relatedNodes.AddRange(ExtractMcpToolReferences(content, mcpToolFiles));
                logger.LogInformation("  {File} -> {Chunks} chunk(s)", relPath, chunks.Count);

                for (var i = 0; i < chunks.Count; i++)
                {
                    var id = BuildChunkDocumentId(projectContext, relPath, chunks.Count, i);
                    var relatedChunkIds = BuildAdjacentChunkIds(projectContext, relPath, chunks.Count, i);

                    await client.IngestDocumentAsync(
                        content: chunks[i],
                        source: relPath,
                        projectContext: projectContext,
                        id: id,
                        relatedNodeIdsCsv: relatedNodes.Count > 0 ? string.Join(",", relatedNodes) : null,
                        relatedDocumentIdsCsv: BuildRelatedDocumentIdsCsv(relatedDocuments, relatedChunkIds),
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

            var normalized = NormalizeLinkTarget(target, sourcePath);
            if (normalized is null)
                continue;

            if (normalized.StartsWith('#'))
                continue;

            references.Add(normalized);
        }

        return references.ToList();
    }

    private static List<string> ExtractCodeFileReferences(string content, string projectContext, string sourcePath)
    {
        var references = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in MarkdownLinkRegex().Matches(content))
        {
            var target = match.Groups["target"].Value.Trim();
            if (string.IsNullOrWhiteSpace(target))
                continue;

            if (target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || target.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                continue;

            var normalized = NormalizeLinkTarget(target, sourcePath);
            if (normalized is null || !IsCodeFile(normalized))
                continue;

            AddCodeFileNodeIdCandidates(references, projectContext, normalized);
        }

        foreach (Match match in InlineCodePathRegex().Matches(content))
        {
            var normalized = NormalizeLinkTarget(match.Value, sourcePath: null);
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

    private static List<string> ExtractRouteReferences(string content, string projectContext)
    {
        var references = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in HttpRouteRegex().Matches(content))
        {
            var method = match.Groups["method"].Value.Trim().ToUpperInvariant();
            var route = match.Groups["route"].Value.Trim();
            if (method.Length == 0 || route.Length == 0)
                continue;

            references.Add($"{projectContext}::ApiEndpoint::{method} {NormalizeRouteTemplate(route)}");
        }

        return references.ToList();
    }

    private static List<string> ExtractMcpToolReferences(string content, IReadOnlyDictionary<string, List<string>> toolFileMap)
    {
        var references = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in McpToolAttributeRegex().Matches(content))
        {
            var toolName = match.Groups["tool"].Value.Trim();
            if (toolName.Length == 0)
                continue;

            if (!toolFileMap.TryGetValue(toolName, out var nodeIds))
                continue;

            foreach (var nodeId in nodeIds)
                references.Add(nodeId);
        }

        return references.ToList();
    }

    private static Dictionary<string, List<string>> BuildMcpToolFileMap(string rootPath, string projectContext)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var toolsRoot = Path.Combine(rootPath, "src", "McpServer", "Tools");
        if (!Directory.Exists(toolsRoot))
            return result;

        foreach (var file in Directory.EnumerateFiles(toolsRoot, "*.cs", SearchOption.AllDirectories))
        {
            var relPath = Path.GetRelativePath(rootPath, file).Replace('\\', '/');
            var content = File.ReadAllText(file);
            foreach (Match match in McpToolAttributeRegex().Matches(content))
            {
                var toolName = match.Groups["tool"].Value.Trim();
                if (toolName.Length == 0)
                    continue;

                if (!result.TryGetValue(toolName, out var nodeIds))
                {
                    nodeIds = [];
                    result[toolName] = nodeIds;
                }

                nodeIds.Add($"{projectContext}:File:{relPath}");
                nodeIds.Add($"{projectContext}::File::{relPath}");
            }
        }

        return result;
    }

    private static string BuildChunkDocumentId(string projectContext, string relPath, int chunkCount, int chunkIndex) =>
        chunkCount == 1
            ? $"{projectContext}::doc::{relPath}"
            : $"{projectContext}::doc::{relPath}::part{chunkIndex + 1}";

    private static List<string> BuildAdjacentChunkIds(string projectContext, string relPath, int chunkCount, int chunkIndex)
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

    private static string? BuildRelatedDocumentIdsCsv(List<string> relatedDocuments, List<string> relatedChunkIds)
    {
        var references = new List<string>(relatedDocuments);
        references.AddRange(relatedChunkIds);

        return references.Count == 0 ? null : string.Join(",", references.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static string? NormalizeLinkTarget(string target, string? sourcePath)
    {
        var normalized = target.Split('#', '?')[0].Trim();
        if (normalized.Length == 0)
            return null;

        var isRootRelative = normalized.StartsWith('/') || normalized.StartsWith('\\');
        normalized = normalized.Replace('\\', '/');
        if (isRootRelative)
            normalized = normalized.TrimStart('/');
        else if (!string.IsNullOrWhiteSpace(sourcePath))
        {
            var sourceDirectory = Path.GetDirectoryName(sourcePath.Replace('/', Path.DirectorySeparatorChar))
                ?.Replace('\\', '/');
            if (!string.IsNullOrWhiteSpace(sourceDirectory))
                normalized = $"{sourceDirectory}/{normalized}";
        }

        var collapsed = CollapseRelativePath(normalized);
        return collapsed.Length == 0 ? null : collapsed;
    }

    private static string CollapseRelativePath(string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var collapsed = new List<string>(segments.Length);

        foreach (var segment in segments)
        {
            if (segment == ".")
                continue;

            if (segment == "..")
            {
                if (collapsed.Count > 0)
                    collapsed.RemoveAt(collapsed.Count - 1);

                continue;
            }

            collapsed.Add(segment);
        }

        return string.Join("/", collapsed);
    }

    private static string NormalizeRouteTemplate(string template)
    {
        var normalized = template.Trim();
        if (Uri.TryCreate(normalized, UriKind.Absolute, out var absoluteUri))
            normalized = absoluteUri.AbsolutePath;

        var queryIndex = normalized.IndexOfAny(['?', '#']);
        if (queryIndex >= 0)
            normalized = normalized[..queryIndex];

        normalized = normalized.Replace('\\', '/');
        normalized = DuplicateSlashRegex().Replace(normalized, "/");
        normalized = RoutePlaceholderRegex().Replace(normalized, "{param}");
        normalized = ColonParameterRegex().Replace(normalized, "/{param}");
        if (!normalized.StartsWith('/'))
            normalized = "/" + normalized;

        normalized = normalized.TrimEnd('/');
        return normalized.Length == 0 ? "/" : normalized.ToLowerInvariant();
    }

    private static bool IsCodeFile(string path) =>
        path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".jsx", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"\[[^\]]+\]\((?<target>[^)]+)\)", RegexOptions.Compiled)]
    private static partial Regex MarkdownLinkRegex();

    [GeneratedRegex(@"(?<![\w])(?:[A-Za-z]:)?(?:\.{1,2}[\\/])?(?:[^\s`'""<>|:/\\]+[\\/])+[^\s`'""<>|:/\\]+\.(?:cs|ts|tsx|js|jsx)\b", RegexOptions.Compiled)]
    private static partial Regex InlineCodePathRegex();

    [GeneratedRegex(@"\b(?<method>GET|POST|PUT|PATCH|DELETE)\s+(?<route>/(?:[^\s`<>)]+))", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex HttpRouteRegex();

    [GeneratedRegex(@"McpServerTool\s*\(\s*Name\s*=\s*""(?<tool>[^""]+)""\s*\)", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex McpToolAttributeRegex();

    [GeneratedRegex("/{2,}")]
    private static partial Regex DuplicateSlashRegex();

    [GeneratedRegex(@"/:[A-Za-z_][A-Za-z0-9_]*")]
    private static partial Regex ColonParameterRegex();

    [GeneratedRegex(@"\{[^}]+\}")]
    private static partial Regex RoutePlaceholderRegex();
}

public sealed record DocumentStats(int DocumentsIndexed);
