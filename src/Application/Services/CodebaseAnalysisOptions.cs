namespace CodeMeridian.Application.Services;

public sealed class CodebaseAnalysisOptions
{
    public StaleKnowledgeOptions StaleKnowledge { get; set; } = new();
    public RankingOptions Ranking { get; set; } = new();
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
        "DependencyInjection"
    ];
}
