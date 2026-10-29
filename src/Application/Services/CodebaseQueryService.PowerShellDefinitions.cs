using CodeMeridian.Core.CodeGraph;

namespace CodeMeridian.Application.Services;

public sealed partial class CodebaseQueryService
{
    private async Task<List<CodeNode>> FilterPowerShellDefinitionsAsync(IEnumerable<CodeNode> nodes,
        string project, string path, CancellationToken cancellationToken)
    {
        var current = await projectProfiles!.GetAsync(project, path, cancellationToken);
        var candidates = nodes.Where(node => node.ProjectContext == project && node.FilePath == path
            && node.Type is CodeNodeType.File or CodeNodeType.Method
            && (node.Id.StartsWith(project + "::PowerShellFile::", StringComparison.Ordinal)
                || node.Id.StartsWith(project + "::PowerShellBody::", StringComparison.Ordinal))
            && node.Properties.GetValueOrDefault("language") == "powershell"
            && node.Properties.GetValueOrDefault("analyzer") == "powershell"
            && node.Properties.GetValueOrDefault("analysisStatus") == "complete").ToList();
        if (candidates.Any(node => !long.TryParse(node.Properties.GetValueOrDefault("powershellGeneration"),
            System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var generation)
            || generation != current.Generation))
            throw new InvalidDataException("PowerShell definitions do not match the current publication generation.");
        return candidates;
    }
}
