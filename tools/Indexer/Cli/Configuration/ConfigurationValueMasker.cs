namespace CodeMeridian.Indexer.Cli.Configuration;

internal static class ConfigurationValueMasker
{
    private static readonly string[] SecretLikeTokens =
    [
        "password",
        "secret",
        "token",
        "apikey",
        "api_key",
        "connectionstring",
        "connection_string",
        "privatekey",
        "private_key"
    ];

    public static bool IsSecretLike(string key) =>
        SecretLikeTokens.Any(token => key.Contains(token, StringComparison.OrdinalIgnoreCase));

    public static string CreatePreview(string key, string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
            return string.Empty;

        if (IsSecretLike(key))
            return "***";

        var normalized = rawValue.Trim().Trim('"', '\'');
        return normalized.Length <= 80 ? normalized : normalized[..80];
    }
}
