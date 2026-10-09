using CodeMeridian.Core.Projects;
using CodeMeridian.Indexer.Cli.Configuration;
using CodeMeridian.Tooling.Discovery;

namespace CodeMeridian.Indexer.Cli.Commands;

internal sealed class LocalProjectProfileBuilder(RepositoryInventoryDiscovery discovery)
{
    private const int MaximumExamples = 5;
    private const int MaximumRoots = 50;

    public ProjectProfile Build(ResolvedIndexerSettings settings, CancellationToken cancellationToken = default)
        => BuildSnapshot(settings, 0, cancellationToken).Profile;

    public ProjectProfileSnapshot BuildSnapshot(ResolvedIndexerSettings settings, long generation,
        CancellationToken cancellationToken = default)
    {
        var inventory = discovery.Discover(settings.RootPath, cancellationToken);
        var observed = inventory.Files.Select(path =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = new FileInfo(Path.Combine(settings.RootPath.FullName, path));
            return (Path: path, Kind: Classify(file, settings.ConfigurationFiles));
        }).ToArray();
        var summaries = observed.GroupBy(file => file.Kind, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new ProjectFileKindSummary(
                group.Key, group.Count(), group.Take(MaximumExamples).Select(file => file.Path).ToArray()))
            .ToArray();
        var roots = new List<ProjectAnalysisRoot>();

        AddRoots("csharp", path => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase), ["csharp"]);
        AddRoots("typescript", path => Path.GetFileName(path).Equals("tsconfig.json", StringComparison.OrdinalIgnoreCase)
            || Path.GetFileName(path).Equals("jsconfig.json", StringComparison.OrdinalIgnoreCase), ["typescript", "javascript"]);
        foreach (var kind in new[] { "markdown", "text", "powershell", "sql", "html", "css", "scss", "configuration" })
        {
            if (observed.Any(file => file.Kind == kind))
                roots.Add(new ProjectAnalysisRoot(kind, ".", null));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var profile = new ProjectProfile(settings.Project, DateTimeOffset.UtcNow,
            inventory.IsComplete ? "complete" : "partial", observed.Length, summaries,
            roots.OrderBy(root => root.Kind, StringComparer.Ordinal).ThenBy(root => root.Path, StringComparer.Ordinal)
                .Take(MaximumRoots).ToArray(), roots.Count,
            BuildAnalyzers(settings), inventory.Warnings, inventory.WarningCount);
        return new ProjectProfileSnapshot(generation, profile,
            observed.Select(file => new ProjectInventoryFile(file.Path, file.Kind)).ToArray());

        void AddRoots(string kind, Func<string, bool> isManifest, string[] sourceKinds)
        {
            var manifests = inventory.Files.Where(isManifest).ToArray();
            foreach (var manifest in manifests)
            {
                var path = Path.GetDirectoryName(manifest)?.Replace('\\', '/') ?? ".";
                if (string.IsNullOrEmpty(path)) path = ".";
                roots.Add(new ProjectAnalysisRoot(kind, path, manifest));
            }
            if (manifests.Length == 0 && observed.Any(file => sourceKinds.Contains(file.Kind, StringComparer.Ordinal)))
                roots.Add(new ProjectAnalysisRoot(kind, ".", null));
        }
    }

    private static string Classify(FileInfo file, IReadOnlyList<string>? configurationFiles)
    {
        if (file.Name.EndsWith(".d.ts", StringComparison.OrdinalIgnoreCase)) return "typescript_declaration";
        if (IndexExecutionPlanBuilder.IsCSharpSourceFile(file)) return "csharp";
        if (IndexExecutionPlanBuilder.IsTypeScriptSourceFile(file))
            return file.Extension.Equals(".js", StringComparison.OrdinalIgnoreCase)
                || file.Extension.Equals(".jsx", StringComparison.OrdinalIgnoreCase) ? "javascript" : "typescript";
        if (IndexExecutionPlanBuilder.IsDocumentationFile(file))
            return file.Extension.Equals(".md", StringComparison.OrdinalIgnoreCase) ? "markdown" : "text";
        return file.Extension.ToLowerInvariant() switch
        {
            ".ps1" or ".psm1" or ".psd1" => "powershell",
            ".sql" => "sql",
            ".html" => "html",
            ".css" => "css",
            ".scss" => "scss",
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".svg" or ".ico"
                or ".pdf" or ".mp3" or ".mp4" or ".woff" or ".woff2" => "asset",
            _ => ConfigurationFilePatternMatcher.IsConfigurationFile(file, configurationFiles) ? "configuration" : "other"
        };
    }

    private static ProjectAnalyzerProfile[] BuildAnalyzers(ResolvedIndexerSettings settings) =>
    [
        Analyzer("csharp", !settings.SkipCSharp, ["callable_definitions", "callable_dependencies"],
            ["Local discovery does not evaluate project files or verify parser/runtime availability."]),
        Analyzer("typescript", !settings.SkipTypeScript, ["callable_definitions", "callable_dependencies"],
            ["JavaScript is included. Declaration files are observed separately.",
             "Local discovery does not verify worker/runtime availability."]),
        Analyzer("frontend", !settings.SkipTypeScript, ["frontend_relationships"],
            ["Local discovery does not establish selector or cascade evidence."]),
        Analyzer("documents", settings.IncludeDocs, ["document_search"],
            ["Searchable document text does not establish complete Markdown reference validation."]),
        Analyzer("configuration", !settings.SkipConfiguration, ["configuration_definitions"],
            ["Configuration usage requires separately indexed code evidence."]),
        Analyzer("sql", settings.Sql?.Enabled == true && !settings.SkipSql, ["sql_object_dependencies"],
            ["SQL indexing is opt-in; supported dialect semantics must be checked separately."]),
        new("powershell", false, "unsupported", "unknown", [], ["PowerShell indexing has not been implemented."])
    ];

    private static ProjectAnalyzerProfile Analyzer(string id, bool enabled, string[] capabilities, string[] limitations) =>
        new(id, true, enabled ? "enabled" : "disabled", "unknown", capabilities, limitations);
}
