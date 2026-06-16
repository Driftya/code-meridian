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
            {
                continue;
            }

            references.Add($"{projectContext}::ApiEndpoint::{method} {NormalizeRouteTemplate(route)}");
        }

        return references.ToArray();
    }

    internal static string NormalizeRouteTemplate(string template)
    {
        var normalized = template.Trim();

        if (Uri.TryCreate(normalized, UriKind.Absolute, out var absoluteUri))
        {
            normalized = absoluteUri.GetComponents(UriComponents.Path, UriFormat.Unescaped);
        }

        normalized = normalized.Replace('\\', '/');

        normalized = EncodedQueryOrFragmentRegex().Replace(normalized, string.Empty);
        normalized = EncodedRoutePlaceholderRegex().Replace(normalized, "{param}");

        normalized = UnescapeRepeatedly(normalized);

        normalized = PercentEncodedQueryMarkerRegex().Replace(normalized, "?");
        normalized = PercentEncodedFragmentMarkerRegex().Replace(normalized, "#");

        var queryIndex = normalized.IndexOfAny(['?', '#']);
        if (queryIndex >= 0)
        {
            normalized = normalized[..queryIndex];
        }

        normalized = EncodedRoutePlaceholderRegex().Replace(normalized, "{param}");
        normalized = RoutePlaceholderRegex().Replace(normalized, "{param}");
        normalized = ColonParameterRegex().Replace(normalized, "/{param}");
        normalized = DuplicateSlashRegex().Replace(normalized, "/");

        if (!normalized.StartsWith('/'))
        {
            normalized = "/" + normalized;
        }

        normalized = normalized.TrimEnd('/');

        return normalized.Length == 0 ? "/" : normalized.ToLowerInvariant();
    }

    private static string UnescapeRepeatedly(string value)
    {
        var current = value;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var unescaped = Uri.UnescapeDataString(current);

            if (StringComparer.Ordinal.Equals(unescaped, current))
            {
                return current;
            }

            current = unescaped;
        }

        return current;
    }

    [GeneratedRegex(@"\b(?<method>GET|POST|PUT|PATCH|DELETE)\s+(?<route>/(?:[^\s`<>)]+))", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex HttpRouteRegex();

    [GeneratedRegex("/{2,}")]
    private static partial Regex DuplicateSlashRegex();

    [GeneratedRegex(@"/:[A-Za-z_][A-Za-z0-9_]*")]
    private static partial Regex ColonParameterRegex();

    [GeneratedRegex(@"\{[^}]+\}")]
    private static partial Regex RoutePlaceholderRegex();

    [GeneratedRegex("(?i)%3f")]
    private static partial Regex PercentEncodedQueryMarkerRegex();

    [GeneratedRegex("(?i)%23")]
    private static partial Regex PercentEncodedFragmentMarkerRegex();

    [GeneratedRegex("(?i)(?:%3f|%23).*$")]
    private static partial Regex EncodedQueryOrFragmentRegex();

    [GeneratedRegex("(?i)%7b[^/?#&\\s]+%7d")]
    private static partial Regex EncodedRoutePlaceholderRegex();
}