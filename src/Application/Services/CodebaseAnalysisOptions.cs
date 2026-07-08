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
    public RoutePlanningOptions RoutePlanning { get; set; } = new();
    public ResponsibilitySliceOptions ResponsibilitySlices { get; set; } = new();
    public PrecisionFeedbackOptions PrecisionFeedback { get; set; } = new();
    public TestCommandOptions TestCommands { get; set; } = new();
}

public sealed class StaleKnowledgeOptions
{
    public List<string> SkipHeuristicSourcePrefixes { get; set; } =
    [
        "docs/plans/",
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

public sealed class RoutePlanningOptions
{
    public bool PreferContractAnchors { get; set; } = false;

    public bool PreferApplicationOrDomainAnchors { get; set; } = true;

    public bool PreferInfrastructureAnchors { get; set; } = false;

    public bool PreferApiAnchors { get; set; } = false;

    public bool PreferCliAnchors { get; set; } = false;

    public int PreferredAnchorBoost { get; set; } = 2;

    public int HighConfidenceCompanionScore { get; set; } = 12;

    public int SameDirectoryCompanionMinimumScore { get; set; } = 6;

    public List<string> ConfigurationGoalTerms { get; set; } =
    [
        "config",
        "configuration",
        "option",
        "options",
        "setting",
        "settings",
        "appsettings",
        ".env",
        "env",
        "environment",
        "docker compose",
        "compose",
        "meridian.json"
    ];
}

public sealed class ResponsibilitySliceOptions
{
    public string DefaultServiceSuffix { get; set; } = "Service";

    public List<PrefixOverrideOptions> NamespaceRootOverrides { get; set; } = [];

    public List<PrefixOverrideOptions> FolderRootOverrides { get; set; } = [];
}

public sealed class PrefixOverrideOptions
{
    public string MatchPrefix { get; set; } = string.Empty;

    public string ReplaceWith { get; set; } = string.Empty;
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

public sealed class TestCommandOptions
{
    public List<TestCommandStrategyOptions> Strategies { get; set; } = [];

    public string? BaseCommand { get; set; }

    public string? SingleTestTemplate { get; set; }

    public string? SameDirectoryTemplate { get; set; }
}

public sealed class TestCommandStrategyOptions
{
    public List<string> MatchFilePathContains { get; set; } = [];

    public string? BaseCommand { get; set; }

    public string? SingleTestTemplate { get; set; }

    public string? SameDirectoryTemplate { get; set; }
}
