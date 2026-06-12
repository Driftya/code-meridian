namespace CodeMeridian.Indexer.Cli.Configuration;

internal static class ConfigurationKeyNormalizer
{
    public static string Normalize(string rawKey)
    {
        var trimmed = rawKey.Trim().Trim('"', '\'');
        return trimmed.Replace("__", ":", StringComparison.Ordinal);
    }
}
