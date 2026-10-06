namespace CodeMeridian.Core.CodeGraph;

/// <summary>A complete SQL file inventory; failed files retain their last successful graph.</summary>
public sealed record SqlGraphSnapshot
{
    public string ContractVersion { get; init; } = "1";
    public required string ProjectContext { get; init; }
    public IReadOnlyList<SqlFileGraph> Files { get; init; } = [];

    public string? Validate()
    {
        if (ContractVersion != "1" || string.IsNullOrWhiteSpace(ProjectContext) || ProjectContext.Length > 256
            || Files is null || Files.Count > 10_000)
            return "Invalid SQL snapshot version, project, or file inventory.";
        var owners = new Dictionary<string, string?>(StringComparer.Ordinal);
        var prefix = ProjectContext + "::Sql::";
        if (Files.Select(f => f?.Path).Distinct(StringComparer.Ordinal).Count() != Files.Count)
            return "Duplicate SQL file path.";
        foreach (var file in Files)
        {
            if (file is null || string.IsNullOrWhiteSpace(file.Path) || file.Path.Length > 1024
                || file.Path.StartsWith('/') || file.Path.Contains('\\') || file.Path.Contains(':')
                || file.Path.Split('/').Any(p => p is ".." or "." or "") || file.Path.Any(char.IsControl)
                || file.Status is not ("complete" or "partial" or "failed")
                || file.Nodes is null || file.Edges is null || file.Diagnostics is null
                || file.Nodes.Count > 100_000 || file.Edges.Count > 200_000
                || file.Status == "failed" && (file.Edges.Count > 0 || file.Nodes.Any(n => n?.Type != CodeNodeType.File))
                || file.Diagnostics.Count > 100 || file.Diagnostics.Any(d => d is null || d.Length > 2048))
                return "Invalid SQL file outcome.";
            foreach (var node in file.Nodes)
            {
                if (node is null || string.IsNullOrWhiteSpace(node.Id) || !node.Id.StartsWith(prefix, StringComparison.Ordinal) || node.Id.Length > 2048
                    || node.ProjectContext != ProjectContext || node.FilePath is not null && node.FilePath != file.Path
                    || node.Type is not (CodeNodeType.File or CodeNodeType.SqlDeclaration or CodeNodeType.SqlReference
                        or CodeNodeType.DatabaseTable or CodeNodeType.DatabaseView or CodeNodeType.DatabaseColumn or CodeNodeType.DatabaseFunction or CodeNodeType.DatabaseRelation)
                    || string.IsNullOrWhiteSpace(node.Name) || node.Name.Length > 1024 || node.Properties is null
                    || node.Type is CodeNodeType.File or CodeNodeType.SqlDeclaration or CodeNodeType.SqlReference && node.FilePath is null
                    || node.Type is not (CodeNodeType.File or CodeNodeType.SqlDeclaration or CodeNodeType.SqlReference) && node.FilePath is not null
                    || !ValidProperties(node.Properties)
                    || owners.TryGetValue(node.Id, out var owner) && owner != node.FilePath)
                    return "Invalid SQL node identity, ownership, or type.";
                owners[node.Id] = node.FilePath;
            }
            var ids = file.Nodes.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var edge in file.Edges)
            {
                if (edge is null || !ids.Contains(edge.SourceId) || !ids.Contains(edge.TargetId)
                    || edge.SourceFilePath != file.Path || edge.ValidateEvidence() is not null
                    || edge.Type is not (CodeEdgeType.Contains or CodeEdgeType.Declares or CodeEdgeType.Reads
                        or CodeEdgeType.Writes or CodeEdgeType.DependsOn or CodeEdgeType.Alters or CodeEdgeType.References or CodeEdgeType.JoinsWith)
                    || edge.Properties is null || !ValidProperties(edge.Properties))
                    return "Invalid SQL edge endpoint, ownership, or evidence.";
            }
        }
        return null;
    }

    private static bool ValidProperties(IReadOnlyDictionary<string, string> properties) =>
        properties.Count <= 32 && properties.All(p => p.Key.Length is > 0 and <= 64
            && !p.Key.StartsWith("sql", StringComparison.OrdinalIgnoreCase) && p.Value is not null && p.Value.Length <= 4096);
}
