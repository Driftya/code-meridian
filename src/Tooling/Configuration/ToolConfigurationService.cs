using DotNetEnv;
using Microsoft.Extensions.Options;
using CodeMeridian.Tooling.Discovery;

namespace CodeMeridian.Tooling.Configuration;

public sealed class ToolConfigurationService(
    CodeMeridianConfigFileStore configFileStore,
    IProjectDiscoveryService projectDiscoveryService,
    IOptions<ToolCliDefaults> defaults) : IToolConfigurationService
{
    private readonly ToolCliDefaults _defaults = defaults.Value;

    public ToolConfigurationContext CreateContext(string? path)
    {
        var rootPath = ResolveRootPath(path);
        LoadDotEnvForInvocation(rootPath);

        return new ToolConfigurationContext(
            rootPath,
            configFileStore.LoadLocal(rootPath),
            configFileStore.LoadGlobal(),
            NormalizeOptionalString(Environment.GetEnvironmentVariable("CodeMeridian_Project")),
            NormalizeOptionalString(Environment.GetEnvironmentVariable("CodeMeridian_Url")),
            NormalizeOptionalString(Environment.GetEnvironmentVariable("CodeMeridian_Auth_ApiKey")));
    }

    public string ResolveProject(
        ToolConfigurationContext context,
        string? overrideProject,
        bool includeFallback = true)
    {
        var resolvedProject =
            NormalizeOptionalString(overrideProject)
            ?? context.EnvironmentProject
            ?? context.LocalConfig?.Project
            ?? context.GlobalConfig?.Project;

        if (!string.IsNullOrWhiteSpace(resolvedProject))
            return resolvedProject;

        return includeFallback ? projectDiscoveryService.ResolveProjectName(context.RootPath) : string.Empty;
    }

    public string ResolveCodeMeridianUrl(ToolConfigurationContext context, string? overrideUrl) =>
        NormalizeOptionalString(overrideUrl)
        ?? context.EnvironmentUrl
        ?? context.LocalConfig?.CodeMeridianUrl
        ?? context.GlobalConfig?.CodeMeridianUrl
        ?? _defaults.DefaultCodeMeridianUrl;

    public bool ResolveAllowRepoScripts(ToolConfigurationContext context, bool allowRepoScriptsOverride) =>
        allowRepoScriptsOverride
        || context.LocalConfig?.AllowRepoScripts == true
        || context.GlobalConfig?.AllowRepoScripts == true;

    public DirectoryInfo ResolveRootPath(string? path)
    {
        var fullPath = string.IsNullOrWhiteSpace(path)
            ? Directory.GetCurrentDirectory()
            : Path.GetFullPath(path, Directory.GetCurrentDirectory());

        return new DirectoryInfo(fullPath);
    }

    private static string? NormalizeOptionalString(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void LoadDotEnvForInvocation(DirectoryInfo rootPath)
    {
        var currentDirectory = new DirectoryInfo(Directory.GetCurrentDirectory());
        LoadClosestDotEnv(currentDirectory);
        if (!string.Equals(currentDirectory.FullName, rootPath.FullName, StringComparison.OrdinalIgnoreCase))
            LoadClosestDotEnv(rootPath);
    }

    private static void LoadClosestDotEnv(DirectoryInfo startDirectory)
    {
        var envFile = FindDotEnv(startDirectory);
        if (envFile is null)
            return;

        Env.Load(
            envFile.FullName,
            new DotNetEnv.LoadOptions(
                setEnvVars: true,
                clobberExistingVars: false,
                onlyExactPath: true));
    }

    private static FileInfo? FindDotEnv(DirectoryInfo directory)
    {
        for (var current = directory; current is not null; current = current.Parent)
        {
            var envFile = new FileInfo(Path.Combine(current.FullName, ".env"));
            if (envFile.Exists)
                return envFile;
        }

        return null;
    }
}
