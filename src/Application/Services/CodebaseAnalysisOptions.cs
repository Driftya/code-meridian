namespace CodeMeridian.Application.Services;

public sealed class CodebaseAnalysisOptions
{
    public StaleKnowledgeOptions StaleKnowledge { get; set; } = new();
    public RankingOptions Ranking { get; set; } = new();
    public CommunityNoiseOptions CommunityNoise { get; set; } = new();
    public CoverageNoiseOptions CoverageNoise { get; set; } = new();
    public DependencyNoiseOptions DependencyNoise { get; set; } = new();
    public SimilarityNoiseOptions SimilarityNoise { get; set; } = new();
    public DuplicateNoiseOptions DuplicateNoise { get; set; } = new();
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

public sealed class CommunityNoiseOptions
{
    public int MinimumCommunitySize { get; set; } = 3;

    public double MinimumProductionMemberRatio { get; set; } = 0.6d;

    public bool IncludeTestCommunities { get; set; } = false;

    public int MaximumVisibleCommunities { get; set; } = 15;

    public int MinimumExtractionCandidateMembers { get; set; } = 3;

    public int MinimumPrimaryExtractionScore { get; set; } = 5;
}

public sealed class CoverageNoiseOptions
{
    public int MinimumHighPriorityLineCount { get; set; } = 8;

    public List<string> LowPriorityNameSuffixes { get; set; } =
    [
        "Dto",
        "Model",
        "Request",
        "Response",
        "Options",
        "Args",
        "Record",
        "Result",
        "Event",
        "Message"
    ];
}

public sealed class DependencyNoiseOptions
{
    public bool IncludeTestDependenciesByDefault { get; set; } = false;
}

public sealed class SimilarityNoiseOptions
{
    public bool PreferSameNodeFamily { get; set; } = true;

    public bool PreferSameLayer { get; set; } = true;

    public double MinimumPrimarySimilarity { get; set; } = 0.85d;
}

public sealed class DuplicateNoiseOptions
{
    public double MinimumPrimarySimilarity { get; set; } = 0.92d;

    public int MaximumPrimaryCombinedFanIn { get; set; } = 4;

    public bool PreferSameLayer { get; set; } = true;
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
