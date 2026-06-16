using System.Text.RegularExpressions;

namespace CodeMeridian.DocumentIndexer.Pipeline;

internal static partial class DocumentRouteReferenceExtractor
{
    public static IReadOnlyList<string> ExtractRouteReferences(string content, string projectContext)
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

        return references.ToArray();
    }

    internal static string NormalizeRouteTemplate(string template)
    {
        var normalized = template.Trim();
        normalized = Uri.UnescapeDataString(normalized);

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

    [GeneratedRegex(@"\b(?<method>GET|POST|PUT|PATCH|DELETE)\s+(?<route>/(?:[^\s`<>)]+))", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex HttpRouteRegex();

    [GeneratedRegex("/{2,}")]
    private static partial Regex DuplicateSlashRegex();

    [GeneratedRegex(@"/:[A-Za-z_][A-Za-z0-9_]*")]
    private static partial Regex ColonParameterRegex();

    [GeneratedRegex(@"\{[^}]+\}")]
    private static partial Regex RoutePlaceholderRegex();
}
