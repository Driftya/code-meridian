namespace CodeMeridian.Application.Services;

public sealed class KeywordClassificationOptions
{
    public const string SectionName = "KeywordClassification";

    public bool Enabled { get; set; } = true;
    public double CommonDocumentFrequencyRatio { get; set; } = 0.35d;
    public int ClassificationVersion { get; set; } = 1;
    public List<string> LexicalConfidenceStopTerms { get; set; } =
    [
        "cancellation",
        "token",
        "async",
        "task",
        "string",
        "int",
        "bool",
        "object",
        "void",
        "class",
        "interface",
        "method",
        "graph",
        "infrastructure",
        "repository",
        "service"
    ];
    public List<string> NoiseTerms { get; set; } =
    [
        "only",
        "when",
        "same",
        "make",
        "want",
        "after",
        "without",
        "through",
        "existing",
        "never",
        "value",
        "name"
    ];

    public List<string> TechnicalTerms { get; set; } =
    [
        "checksum",
        "token",
        "docker",
        "powershell",
        "json",
        "toml",
        "cancellation",
        "logging",
        "structured"
    ];

    public List<string> ToolingTerms { get; set; } =
    [
        "mcp",
        "cli",
        "sdk",
        "indexer",
        "codex",
        "doctor",
        "rebuild"
    ];

    public List<string> ArchitectureTerms { get; set; } =
    [
        "domain",
        "application",
        "infrastructure",
        "repository",
        "options",
        "configuration"
    ];

    public List<string> DiagnosticTerms { get; set; } =
    [
        "diagnostic",
        "diagnostics",
        "error",
        "warning",
        "severity",
        "lint",
        "verify"
    ];

    public List<string> DomainTerms { get; set; } =
    [
        "lexical",
        "enrichment",
        "heuristic",
        "knowledge",
        "stale",
        "idempotent",
        "related"
    ];
}
