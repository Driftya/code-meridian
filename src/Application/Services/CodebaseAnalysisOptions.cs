namespace CodeMeridian.Application.Services;

public sealed class CodebaseAnalysisOptions
{
    public StaleKnowledgeOptions StaleKnowledge { get; set; } = new();
    public RankingOptions Ranking { get; set; } = new();
    public PrecisionFeedbackOptions PrecisionFeedback { get; set; } = new();
}

public sealed class StaleKnowledgeOptions
{
    public List<string> SkipHeuristicSourcePrefixes { get; set; } =
    [
        "docs/plan/",
        "docs/features/"
    ];

    public List<string> IgnoredMentionTokens { get; set; } =
    [
        "ASP",
        "ASP.NET",
        "CodeMeridian.Indexer",
        "CodeMeridian.Indexer.Tests",
        "TypeScript",
        "JavaScript",
        "Node.js",
        "Neo4j",
        "Docker",
        "README",
        "README.md",
        "meridian.json",
        "mcp.json",
        "config.toml",
        "compose.codemeridian.yml",
        "docker-compose.yml",
        "ApiEndpoint",
        "Controller",
        "Command",
        "Import",
        "Identify",
        "Configure",
        "Constants",
        "Resolve",
        "MapGet",
        "MapPost",
        "MapPut",
        "MapDelete",
        "Expected",
        "Actual",
        "Example",
        "Examples",
        "Warning",
        "Warnings",
        "Notes"
    ];

    public List<string> CodeLikeSuffixes { get; set; } =
    [
        "Service",
        "Repository",
        "Controller",
        "Handler",
        "Provider",
        "Factory",
        "Client",
        "Command",
        "Query",
        "Endpoint",
        "Endpoints",
        "Tool",
        "Tools",
        "Options",
        "Registry",
        "Context",
        "Builder",
        "Mapper",
        "Validator",
        "Exception",
        "Reader",
        "Writer",
        "Parser",
        "Resolver",
        "Indexer",
        "Ingester"
    ];

    public List<string> IgnoredDottedSuffixes { get; set; } =
    [
        ".com",
        ".org",
        ".net",
        ".md",
        ".json",
        ".toml",
        ".yml",
        ".yaml",
        ".txt",
        ".sln",
        ".csproj",
        ".tsproj"
    ];
}

public sealed class RankingOptions
{
    public bool PreferProductionOverTests { get; set; } = true;

    public bool ProductionOnlyByDefault { get; set; } = true;

    public bool IncludeBroaderHeuristicMatches { get; set; } = false;

    public bool IncludeSuppressedNoise { get; set; } = false;

    public int MinimumActionableLineCount { get; set; } = 3;

    public List<string> TestPathContains { get; set; } =
    [
        "test",
        ".spec.",
        ".test."
    ];

    public List<string> InfrastructureNameSuffixes { get; set; } =
    [
        "Options",
        "Endpoints",
        "Tools"
    ];

    public List<string> InfrastructureNames { get; set; } =
    [
        "DependencyInjection",
        "Program",
        "Startup",
        "CompositionRoot",
        "ServiceCollectionExtensions",
        "StartupExtensions",
        "ConfigureServices",
        "Module",
        "AppModule",
        "ContainerModule"
    ];

    public List<string> SuppressedFileRoles { get; set; } =
    [
        "Test",
        "Configuration",
        "Migration",
        "Snapshot",
        "Generated",
        "BuildArtifact"
    ];

    public List<string> SuppressedNodeTypes { get; set; } =
    [
        "ConfigurationKey",
        "ConfigurationEntry",
        "Diagnostic",
        "Property",
        "Field",
        "Event",
        "Indexer",
        "Operator"
    ];

    public List<string> BroaderHeuristicNodeTypes { get; set; } =
    [
        "Namespace",
        "File",
        "ApiEndpoint",
        "ConfigurationFile",
        "DatabaseTable",
        "ExternalConcept",
        "MessageTopic",
        "ExternalService"
    ];
}

public sealed class PrecisionFeedbackOptions
{
    public bool Enabled { get; set; } = true;

    public string FeedbackFilePath { get; set; } = ".meridian/precision-feedback.json";

    public int AcceptedFileBoost { get; set; } = 4;

    public int AcceptedTestBoost { get; set; } = 2;

    public int IgnoredFilePenalty { get; set; } = 3;

    public int FileOnlyPenalty { get; set; } = 2;

    public int HeuristicPenalty { get; set; } = 1;

    public int StalePenalty { get; set; } = 2;
}
