namespace CodeMeridian.Indexer.Cli.Configuration;

internal static class ConfigurationFilePatternMatcher
{
    private static readonly string[] DefaultPatterns =
    [
        ".env",
        "appsettings.json",
        "appsettings.*.json",
        "meridian.json",
        "meridian.sample.json",
        "docker-compose.yml",
        "docker-compose.yaml",
        "docker-compose.sample.yml",
        "docker-compose.sample.yaml",
        "*.json",
        "*.yml",
        "*.yaml"
    ];

    public static IReadOnlyList<string> DefaultConfigurationFiles => DefaultPatterns;

    public static bool IsConfigurationFile(FileInfo file, IReadOnlyList<string>? patterns = null)
    {
        var effectivePatterns = patterns is { Count: > 0 } ? patterns : DefaultPatterns;
        return effectivePatterns.Any(pattern => MatchesPattern(file.Name, pattern));
    }

    private static bool MatchesPattern(string fileName, string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return false;

        var normalizedPattern = pattern.Trim();
        if (!normalizedPattern.Contains('*', StringComparison.Ordinal))
            return fileName.Equals(normalizedPattern, StringComparison.OrdinalIgnoreCase);

        var segments = normalizedPattern.Split('*');
        var cursor = 0;
        var anchoredAtStart = !normalizedPattern.StartsWith('*');
        var anchoredAtEnd = !normalizedPattern.EndsWith('*');

        for (var index = 0; index < segments.Length; index++)
        {
            var segment = segments[index];
            if (segment.Length == 0)
                continue;

            var matchIndex = fileName.IndexOf(segment, cursor, StringComparison.OrdinalIgnoreCase);
            if (matchIndex < 0)
                return false;

            if (index == 0 && anchoredAtStart && matchIndex != 0)
                return false;

            cursor = matchIndex + segment.Length;
        }

        if (anchoredAtEnd)
        {
            var lastSegment = segments[^1];
            if (lastSegment.Length > 0 && !fileName.EndsWith(lastSegment, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    public static int GetOrder(FileInfo file)
    {
        var name = file.Name;
        if (name.Equals("appsettings.json", StringComparison.OrdinalIgnoreCase))
            return 0;

        if (name.StartsWith("appsettings.", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            return 1;

        if (name.Equals("meridian.json", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("meridian.sample.json", StringComparison.OrdinalIgnoreCase))
            return 2;

        if (file.Extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
            return 3;

        if (file.Name.Equals(".env", StringComparison.OrdinalIgnoreCase))
            return 4;

        return 5;
    }
}
