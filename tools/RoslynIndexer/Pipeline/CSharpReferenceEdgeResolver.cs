namespace CodeMeridian.RoslynIndexer.Pipeline;

internal static class CSharpReferenceEdgeResolver
{
    public static List<IngestEdgeRequest> Resolve(
        IReadOnlyList<IngestNodeRequest> nodes,
        IReadOnlyList<IngestEdgeRequest> edges)
    {
        var nodesById = nodes
            .GroupBy(n => n.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var typeCandidates = nodes
            .Where(n => n.Type is "Class" or "Interface" or "Enum" or "Struct" or "RecordClass" or "RecordStruct" or "Delegate")
            .Select(n => new TypeCandidate(n.Id, n.Type, n.Namespace, n.FilePath, n.Name, ShortTypeName(n.Id)))
            .GroupBy(n => (n.Type, n.Name), StringTupleComparer.OrdinalType)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringTupleComparer.OrdinalType);
        var typeCandidatesByName = nodes
            .Where(n => n.Type is "Class" or "Interface" or "Enum" or "Struct" or "RecordClass" or "RecordStruct" or "Delegate")
            .Select(n => new TypeCandidate(n.Id, n.Type, n.Namespace, n.FilePath, n.Name, ShortTypeName(n.Id)))
            .GroupBy(n => n.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);

        var resolved = new List<IngestEdgeRequest>(edges.Count);
        foreach (var edge in edges)
        {
            if (edge.RelationshipType is not ("Uses" or "Implements" or "Inherits"))
            {
                resolved.Add(edge);
                continue;
            }

            if (nodesById.ContainsKey(edge.TargetId))
            {
                resolved.Add(edge);
                continue;
            }

            if (!nodesById.TryGetValue(edge.SourceId, out var source) || edge.TargetName is null || edge.TargetType is null)
                continue;

            if (!typeCandidates.TryGetValue((edge.TargetType, edge.TargetName), out var candidates) &&
                !typeCandidatesByName.TryGetValue(edge.TargetName, out candidates))
                continue;

            var selected = SelectBestTypeCandidate(source, candidates);
            if (selected is not null)
                resolved.Add(edge with { TargetId = selected.Id });
        }

        return resolved
            .Where(edge => !string.IsNullOrWhiteSpace(edge.TargetId))
            .DistinctBy(BuildEdgeIdentity, StringComparer.Ordinal)
            .ToList();
    }

    private static string BuildEdgeIdentity(IngestEdgeRequest edge) =>
        edge.RelationshipType is "ReadsConfig" or "BindsConfig"
            ? $"{edge.SourceId}|{edge.TargetId}|{edge.RelationshipType}|{ReadProperty(edge, "accessPattern")}"
            : $"{edge.SourceId}|{edge.TargetId}|{edge.RelationshipType}";

    private static string? ReadProperty(IngestEdgeRequest edge, string key) =>
        edge.Properties is not null && edge.Properties.TryGetValue(key, out var value) ? value : null;

    private static TypeCandidate? SelectBestTypeCandidate(
        IngestNodeRequest source,
        IReadOnlyList<TypeCandidate> candidates)
    {
        if (candidates.Count == 1)
            return candidates[0];

        var sameNamespace = candidates
            .Where(candidate => string.Equals(candidate.Namespace, source.Namespace, StringComparison.Ordinal))
            .ToArray();
        if (sameNamespace.Length == 1)
            return sameNamespace[0];

        var sameFile = candidates
            .Where(candidate => string.Equals(candidate.FilePath, source.FilePath, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return sameFile.Length == 1 ? sameFile[0] : null;
    }

    private static string ShortTypeName(string id)
    {
        var name = id.Split("::").LastOrDefault() ?? id;
        var dot = name.LastIndexOf('.');
        return dot >= 0 ? name[(dot + 1)..] : name;
    }
}
