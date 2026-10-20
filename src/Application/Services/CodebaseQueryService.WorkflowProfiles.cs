using CodeMeridian.Application.Projects;
using CodeMeridian.Core.Projects;

namespace CodeMeridian.Application.Services;

public sealed partial class CodebaseQueryService
{
    private async Task<ProjectProfileResult?> GetWorkflowProfileAsync(string? project, string? target, CancellationToken cancellationToken)
    {
        if (projectProfiles is null) return null; // Compatibility for callers constructing the legacy service without profiles.
        if (string.IsNullOrWhiteSpace(project))
            return new ProjectProfileResult("unknown", "unspecified", null, null, null, null, null,
                ["Supply an explicit project context; another project's capabilities must not be substituted."]);
        string? targetPath = null;
        if (target is not null && !target.Contains(':') && (target.Contains('/') || target.Contains('\\')
            || target is ".env" or "Dockerfile"
            || new[] { ".cs", ".ts", ".tsx", ".js", ".jsx", ".md", ".txt", ".ps1", ".psm1", ".psd1", ".sql", ".json", ".html", ".css", ".scss", ".yaml", ".yml", ".toml", ".xml", ".ini", ".config", ".csproj", ".py" }
                .Contains(Path.GetExtension(target), StringComparer.OrdinalIgnoreCase)))
            targetPath = ProjectProfilePath.Normalize(target);
        var explicitPath = targetPath is not null;
        if (targetPath is null && !string.IsNullOrWhiteSpace(target) && !target.Contains(':'))
        {
            try { targetPath = ProjectProfilePath.Normalize(target); }
            catch (ArgumentException) { } // A symbol/dependency hint need not be a valid inventory path.
        }
        try
        {
            var profile = await projectProfiles.GetAsync(project, targetPath, cancellationToken);
            if (!explicitPath && profile.Target?.Status != "observed") profile = profile with { Target = null };
            if (target?.Contains("::", StringComparison.Ordinal) == true && profile.Status == "available")
            {
                var node = (await codeGraph.GetContextForEditingAsync(target, cancellationToken))?.Node;
                if (node?.ProjectContext == project && node.FilePath is { } path)
                    return await projectProfiles.GetAsync(project, path, cancellationToken);
                return profile with { Target = new(target, "unknown", null) };
            }
            return profile;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ProjectProfileResult("failed", project, null, null, null, null, null,
                ["Project capability evidence could not be read; no analysis was authorized."]);
        }
    }
}
