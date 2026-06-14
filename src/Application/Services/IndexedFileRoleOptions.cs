using CodeMeridian.Core.CodeGraph;

namespace CodeMeridian.Application.Services;

public sealed class CodebaseIndexingOptions
{
    public FileRolePatternOptions FileRoles { get; set; } = FileRolePatternOptions.CreateDefaults();
}

public sealed class FileRolePatternOptions
{
    public List<string> Test { get; set; } =
    [
        "tests/**/*.cs",
        "test/**/*.cs",
        "**/*.Tests/**/*.cs",
        "**/*.Test/**/*.cs",
        "**/*Tests.cs",
        "**/*.test.ts",
        "**/*.spec.ts",
        "**/*.test.tsx",
        "**/*.spec.tsx"
    ];

    public List<string> Migration { get; set; } =
    [
        "**/Migrations/*.cs",
        "**/Migrations/**/*.cs"
    ];

    public List<string> Snapshot { get; set; } =
    [
        "**/*ModelSnapshot.cs"
    ];

    public List<string> Generated { get; set; } =
    [
        "**/*.g.cs",
        "**/*.generated.cs",
        "**/*.Designer.cs",
        "**/*.designer.cs",
        "**/openapi.generated.ts",
        "**/graphql.generated.ts"
    ];

    public List<string> BuildArtifact { get; set; } =
    [
        "**/bin/**",
        "**/obj/**",
        "**/node_modules/**",
        "**/dist/**",
        "**/build/**",
        "**/coverage/**"
    ];

    public List<string> Documentation { get; set; } =
    [
        "**/*.md",
        "**/*.mdx",
        "**/*.txt"
    ];

    public List<string> Configuration { get; set; } =
    [
        "**/appsettings.json",
        "**/appsettings.*.json",
        "**/meridian.json",
        "**/meridian.sample.json",
        "**/.env",
        "**/docker-compose*.yml",
        "**/docker-compose*.yaml"
    ];

    public static FileRolePatternOptions CreateDefaults() => new();

    public IReadOnlyList<string> GetPatterns(IndexedFileRole role) => role switch
    {
        IndexedFileRole.Test => Test,
        IndexedFileRole.Migration => Migration,
        IndexedFileRole.Snapshot => Snapshot,
        IndexedFileRole.Generated => Generated,
        IndexedFileRole.BuildArtifact => BuildArtifact,
        IndexedFileRole.Documentation => Documentation,
        IndexedFileRole.Configuration => Configuration,
        _ => []
    };
}
