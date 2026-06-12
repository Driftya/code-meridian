using System.Text.RegularExpressions;

namespace CodeMeridian.DocumentIndexer.Pipeline;

internal static partial class DocumentReferenceExtractor
{
    public static IReadOnlyList<string> ExtractDocumentReferences(string content, string sourcePath) =>
        ExtractMarkdownLinkTargets(content, sourcePath)
            .Where(target => !target.StartsWith('#'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    internal static IEnumerable<string> ExtractMarkdownLinkTargets(string content, string sourcePath)
    {
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

            yield return normalized;
        }
    }

    internal static string? NormalizeLinkTarget(string target, string? sourcePath)
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

    [GeneratedRegex(@"\[[^\]]+\]\((?<target>[^)]+)\)", RegexOptions.Compiled)]
    private static partial Regex MarkdownLinkRegex();
}
