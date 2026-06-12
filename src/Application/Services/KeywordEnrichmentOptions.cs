namespace CodeMeridian.Application.Services;

public sealed class KeywordEnrichmentOptions
{
    public const string SectionName = "KeywordEnrichment";

    public bool Enabled { get; set; } = true;
    public int MinimumKeywordLength { get; set; } = 4;
    public int MaximumKeywordsPerNode { get; set; } = 40;
    public int MinimumSharedKeywords { get; set; } = 3;
    public double MinimumScore { get; set; } = 0.25d;
    public double MaximumDocumentFrequencyRatio { get; set; } = 0.35d;
    public int BatchSize { get; set; } = 500;
    public int EnrichmentVersion { get; set; } = 1;

    public List<string> AllowedShortTerms { get; set; } =
    [
        "api",
        "mcp",
        "cli",
        "sdk",
        "jwt",
        "sql",
        "ast",
        "ef",
        "ts"
    ];

    public List<string> AdditionalStopwords { get; set; } = [];
}
