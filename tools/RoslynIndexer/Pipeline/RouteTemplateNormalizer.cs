using System.Text.RegularExpressions;

namespace CodeMeridian.RoslynIndexer.Pipeline;

internal static partial class RouteTemplateNormalizer
{
    public static string Normalize(string template)
    {
        var normalized = template.Trim();
        if (Uri.TryCreate(normalized, UriKind.Absolute, out var absoluteUri))
            normalized = Uri.UnescapeDataString(absoluteUri.AbsolutePath);

        var queryIndex = normalized.IndexOfAny(['?', '#']);
        if (queryIndex >= 0)
            normalized = normalized[..queryIndex];

        normalized = normalized.Replace('\\', '/');
        normalized = DuplicateSlashRegex().Replace(normalized, "/");
        if (!normalized.StartsWith('/'))
            normalized = "/" + normalized;

        normalized = RoutePlaceholderRegex().Replace(normalized, "{param}");
        normalized = ColonParameterRegex().Replace(normalized, "/{param}");
        normalized = normalized.TrimEnd('/');
        if (normalized.Length == 0)
            normalized = "/";

        return normalized.ToLowerInvariant();
    }

    [GeneratedRegex("/{2,}")]
    private static partial Regex DuplicateSlashRegex();

    [GeneratedRegex(@"/:[A-Za-z_][A-Za-z0-9_]*")]
    private static partial Regex ColonParameterRegex();

    [GeneratedRegex(@"\{[^}]+\}")]
    private static partial Regex RoutePlaceholderRegex();
}
