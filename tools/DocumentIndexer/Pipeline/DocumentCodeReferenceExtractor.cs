using System.Text.RegularExpressions;

namespace CodeMeridian.DocumentIndexer.Pipeline;

internal static partial class DocumentCodeReferenceExtractor
{
    public static IReadOnlyList<string> ExtractCodeFileReferences(string content, string projectContext, string sourcePath)
    {
        var references = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var target in DocumentReferenceExtractor.ExtractMarkdownLinkTargets(content, sourcePath))
        {
            if (!IsCodeFile(target))
                continue;

            AddCodeFileNodeIdCandidates(references, projectContext, target);
        }

        foreach (Match match in InlineCodePathRegex().Matches(content))
        {
            if (match.Value.Contains('(') || match.Value.Contains('[') || match.Value.Contains(']'))
                continue;

            var normalized = DocumentReferenceExtractor.NormalizeLinkTarget(match.Value, sourcePath: null);
            if (normalized is null || !IsCodeFile(normalized))
                continue;

            AddCodeFileNodeIdCandidates(references, projectContext, normalized);
        }

        return references.ToArray();
    }

    private static void AddCodeFileNodeIdCandidates(HashSet<string> references, string projectContext, string relativePath)
    {
        references.Add($"{projectContext}:File:{relativePath}");
        references.Add($"{projectContext}::File::{relativePath}");
    }

    private static bool IsCodeFile(string path) =>
        path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".jsx", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"(?<![\w])(?:[A-Za-z]:)?(?:\.{1,2}[\\/])?(?:[^\s`'""<>|:/\\]+[\\/])+[^\s`'""<>|:/\\]+\.(?:cs|ts|tsx|js|jsx)\b", RegexOptions.Compiled)]
    private static partial Regex InlineCodePathRegex();

}
