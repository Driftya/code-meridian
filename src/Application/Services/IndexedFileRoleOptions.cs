using CodeMeridian.Core.CodeGraph;

namespace CodeMeridian.Application.Services;

public sealed class CodebaseIndexingOptions
{
    public FileRolePatternOptions FileRoles { get; set; } = FileRolePatternOptions.CreateDefaults();
}

public sealed class FileRolePatternOptions
{
    public List<string> Test { get; set; } = [.. CSharpTestPatterns, .. TypeScriptTestPatterns];

    public List<string> Migration { get; set; } = [.. CSharpMigrationPatterns];

    public List<string> Snapshot { get; set; } = [.. CSharpSnapshotPatterns];

    public List<string> Generated { get; set; } = [.. CSharpGeneratedPatterns, .. TypeScriptGeneratedPatterns];

    public List<string> BuildArtifact { get; set; } = [.. SharedBuildArtifactPatterns];

    public List<string> Configuration { get; set; } =
    [
        .. CSharpConfigurationPatterns,
        .. TypeScriptConfigurationSuffixPatterns,
        .. TypeScriptConfigurationDotfilePatterns
    ];

    public static FileRolePatternOptions CreateDefaults() => new();

    public IReadOnlyList<string> GetPatterns(IndexedFileRole role) => role switch
    {
        IndexedFileRole.Test => Test,
        IndexedFileRole.Migration => Migration,
        IndexedFileRole.Snapshot => Snapshot,
        IndexedFileRole.Generated => Generated,
        IndexedFileRole.BuildArtifact => BuildArtifact,
        IndexedFileRole.Configuration => Configuration,
        _ => []
    };

    private static readonly string[] CSharpTestPatterns =
    [
        "tests/**/*.cs",
        "test/**/*.cs",
        "**/*.Tests/**/*.cs",
        "**/*.Test/**/*.cs",
        "**/*Tests.cs"
    ];

    private static readonly string[] TypeScriptTestPatterns =
    [
        "tests/**/*.ts",
        "tests/**/*.tsx",
        "tests/**/*.js",
        "tests/**/*.jsx",
        "test/**/*.ts",
        "test/**/*.tsx",
        "test/**/*.js",
        "test/**/*.jsx",
        "**/*.test.ts",
        "**/*.spec.ts",
        "**/*.test.tsx",
        "**/*.spec.tsx"
    ];

    private static readonly string[] CSharpMigrationPatterns =
    [
        "**/Migrations/*.cs",
        "**/Migrations/**/*.cs"
    ];

    private static readonly string[] CSharpSnapshotPatterns =
    [
        "**/*ModelSnapshot.cs"
    ];

    private static readonly string[] CSharpGeneratedPatterns =
    [
        "**/*.g.cs",
        "**/*.generated.cs",
        "**/*.Designer.cs",
        "**/*.designer.cs"
    ];

    private static readonly string[] TypeScriptGeneratedPatterns =
    [
        "**/openapi.generated.ts",
        "**/graphql.generated.ts"
    ];

    private static readonly string[] SharedBuildArtifactPatterns =
    [
        "**/bin/**",
        "**/obj/**",
        "**/node_modules/**",
        "**/dist/**",
        "**/build/**",
        "**/coverage/**"
    ];

    private static readonly string[] CSharpConfigurationPatterns =
    [
        "**/*Options.cs",
        "**/*Option.cs",
        "**/*Configuration.cs",
        "**/*Configurations.cs",
        "**/*Config.cs",
        "**/*Configs.cs",
        "**/*Setting.cs",
        "**/*Settings.cs"
    ];

    private static readonly string[] TypeScriptConfigurationSuffixPatterns =
    [
        "**/*Options.ts",
        "**/*Option.ts",
        "**/*Configuration.ts",
        "**/*Configurations.ts",
        "**/*Config.ts",
        "**/*Configs.ts",
        "**/*Setting.ts",
        "**/*Settings.ts",
        "**/*Options.tsx",
        "**/*Option.tsx",
        "**/*Configuration.tsx",
        "**/*Configurations.tsx",
        "**/*Config.tsx",
        "**/*Configs.tsx",
        "**/*Setting.tsx",
        "**/*Settings.tsx"
    ];

    private static readonly string[] TypeScriptConfigurationDotfilePatterns =
    [
        "**/*.config.ts",
        "**/*.configs.ts",
        "**/*.configuration.ts",
        "**/*.configurations.ts",
        "**/*.option.ts",
        "**/*.options.ts",
        "**/*.setting.ts",
        "**/*.settings.ts",
        "**/*.config.tsx",
        "**/*.configs.tsx",
        "**/*.configuration.tsx",
        "**/*.configurations.tsx",
        "**/*.option.tsx",
        "**/*.options.tsx",
        "**/*.setting.tsx",
        "**/*.settings.tsx"
    ];
}
