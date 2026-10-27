using CodeMeridian.Core.Projects;

namespace CodeMeridian.Core.CodeGraph;

/// <summary>A full selected inventory, rebound together. Failed sources retain stale owned facts.</summary>
public sealed record PowerShellGraphSnapshot(string Project, long Generation, string SourceFingerprint,
    ProjectAnalyzerFingerprint Fingerprint, IReadOnlyList<PowerShellFileGraph> Files)
{
    public string ContractVersion { get; init; } = "1.0";

    public string? Validate()
    {
        if (ContractVersion != "1.0" || string.IsNullOrWhiteSpace(Project) || Project.Length > 200
            || Project != Project.Trim() || Project.Contains("::", StringComparison.Ordinal) || Project.Any(char.IsControl)
            || Generation <= 0 || !ProjectAnalyzerFingerprint.ValidHash(SourceFingerprint)
            || Fingerprint is null || Fingerprint.Validate() is not null || Files is null || Files.Count > 1000)
            return "Invalid PowerShell publication identity, fingerprint, or inventory.";
        var nodes = new Dictionary<string, CodeNode>(StringComparer.Ordinal);
        var paths = new HashSet<string>(StringComparer.Ordinal);
        var edgeCount = 0;
        foreach (var file in Files)
        {
            if (file is null) return "A PowerShell file outcome is required.";
            try
            {
                if (ProjectProfilePath.Normalize(file.Path) != file.Path || !paths.Add(file.Path)
                    || System.IO.Path.GetExtension(file.Path).ToLowerInvariant() is not (".ps1" or ".psm1" or ".psd1"))
                    return "PowerShell sources must have unique canonical script/module/manifest paths.";
            }
            catch (ArgumentException) { return "Invalid PowerShell source path."; }
            if (file.Status is not ("complete" or "partial" or "failed") || file.Nodes is null || file.Edges is null
                || file.Nodes.Count > 20000 || file.Edges.Count > 20000
                || file.Status == "failed" && (file.Nodes.Count != 0 || file.Edges.Count != 0)
                || file.Status != "failed" && !ProjectAnalyzerFingerprint.ValidHash(file.SourceFingerprint)
                || file.SourceFingerprint is not null && !ProjectAnalyzerFingerprint.ValidHash(file.SourceFingerprint))
                return "Invalid PowerShell file status, source binding, or fact budget.";
            foreach (var node in file.Nodes)
            {
                if (node is null || node.Id is null || node.Id.Length > 256
                    || !(node.Id.StartsWith(Project + "::PowerShellFile::", StringComparison.Ordinal)
                        || node.Id.StartsWith(Project + "::PowerShellBody::", StringComparison.Ordinal))
                    || node.Type is not (CodeNodeType.File or CodeNodeType.Method) || node.ProjectContext != Project
                    || node.FilePath != file.Path || node.SourceHash != file.SourceFingerprint
                    || string.IsNullOrWhiteSpace(node.Name) || node.Name.Length > 1024
                    || node.SourceSnippet?.Length > 4096 || node.Summary is not null || node.Embedding is not null
                    || node.Namespace is not null || !ValidProperties(node.Properties) || !nodes.TryAdd(node.Id, node))
                    return "Invalid PowerShell node identity, source ownership, or metadata.";
            }
            if (file.Status != "failed" && file.Nodes.Count(node => node.Type == CodeNodeType.File) != 1)
                return "Each analyzed PowerShell file requires exactly one file node.";
            edgeCount += file.Edges.Count;
        }
        if (nodes.Count + edgeCount > 40000) return "PowerShell publication exceeds its total fact budget.";
        foreach (var file in Files)
        foreach (var edge in file.Edges)
        {
            if (edge is null || edge.SourceId is null || edge.TargetId is null || !nodes.TryGetValue(edge.SourceId, out var source) || !nodes.ContainsKey(edge.TargetId)
                || source.FilePath != file.Path || edge.SourceFilePath != file.Path
                || edge.Type is not (CodeEdgeType.Contains or CodeEdgeType.Calls or CodeEdgeType.DependsOn)
                || edge.ValidateEvidence() is not null || !ValidProperties(edge.Properties)
                || edge.Confidence is { } confidence && (!double.IsFinite(confidence) || confidence is < 0 or > 1)
                || edge.Type != CodeEdgeType.Contains && (edge.EvidenceKind != EdgeEvidenceKind.Inferred || edge.Resolver != "powershell_static_v1"))
                return "Invalid PowerShell edge ownership, endpoint, or evidence.";
        }
        return null;
    }

    public string? ValidateAgainst(ProjectProfile profile, IReadOnlyList<ProjectInventoryFile> inventory)
    {
        var analyzer = profile.Analyzers.SingleOrDefault(item => item.Id == "powershell");
        if (profile.Project != Project || profile.SourceFingerprint != SourceFingerprint
            || analyzer is not { Supported: true, ConfigurationState: "enabled" } || analyzer.Fingerprint != Fingerprint)
            return "PowerShell publication must match the enabled analyzer and source identities of the current profile.";
        return inventory.Where(file => file.Kind == "powershell").Select(file => file.Path).Order(StringComparer.Ordinal)
            .SequenceEqual(Files.Select(file => file.Path).Order(StringComparer.Ordinal), StringComparer.Ordinal)
            ? null : "PowerShell publication must cover the complete current PowerShell inventory.";
    }

    private static bool ValidProperties(Dictionary<string, string>? properties) => properties is { Count: <= 16 }
        && properties.All(pair => pair.Key.Length is > 0 and <= 64 && pair.Value is { Length: <= 4096 }
            && pair.Key is "language" or "powershellKind" or "analysisStatus" or "analyzer"
                or "lexicalParentId" or "conditional" or "powershellInvocation");
}
