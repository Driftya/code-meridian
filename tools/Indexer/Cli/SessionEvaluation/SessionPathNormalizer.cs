namespace CodeMeridian.Indexer.Cli.SessionEvaluation;

internal static class SessionPathNormalizer
{
    public static string Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        return path.Trim()
            .Replace('\\', '/')
            .TrimStart('.', '/');
    }

    public static bool IsLikelyTestPath(string path)
    {
        var normalized = Normalize(path);
        return normalized.Contains("/test/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/tests/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains(".test.", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains(".tests.", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains(".spec.", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith("Tests.cs", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith("Test.cs", StringComparison.OrdinalIgnoreCase);
    }
}
