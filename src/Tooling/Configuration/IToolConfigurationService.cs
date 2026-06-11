namespace CodeMeridian.Tooling.Configuration;

public interface IToolConfigurationService
{
    ToolConfigurationContext CreateContext(string? path);
    string ResolveProject(ToolConfigurationContext context, string? overrideProject, bool includeFallback = true);
    string ResolveCodeMeridianUrl(ToolConfigurationContext context, string? overrideUrl);
    bool ResolveAllowRepoScripts(ToolConfigurationContext context, bool allowRepoScriptsOverride);
    DirectoryInfo ResolveRootPath(string? path);
}
