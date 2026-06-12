using System.Text.RegularExpressions;

namespace CodeMeridian.DocumentIndexer.Pipeline;

internal static partial class DocumentMcpToolReferenceExtractor
{
    public static IReadOnlyDictionary<string, List<string>> BuildMcpToolFileMap(string rootPath, string projectContext)
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

    public static IReadOnlyList<string> ExtractMcpToolReferences(string content, IReadOnlyDictionary<string, List<string>> toolFileMap)
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

        return references.ToArray();
    }

    [GeneratedRegex(@"McpServerTool\s*\(\s*Name\s*=\s*""(?<tool>[^""]+)""\s*\)", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex McpToolAttributeRegex();
}
