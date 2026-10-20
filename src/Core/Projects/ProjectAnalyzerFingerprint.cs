namespace CodeMeridian.Core.Projects;

public sealed record ProjectAnalyzerFingerprint(string Implementation, string Parser)
{
    public string? Validate() => ValidHash(Implementation) && ValidHash(Parser)
        ? null : "Analyzer fingerprints must be SHA-256 hashes.";

    internal static bool ValidHash(string? value) => value is { Length: 71 }
        && value.StartsWith("sha256:", StringComparison.Ordinal) && value.Skip(7).All(char.IsAsciiHexDigit);
}
