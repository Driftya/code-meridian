namespace CodeMeridian.RoslynIndexer.Pipeline;

internal static class CSharpReferenceEdgeResolver
{
    public static List<IngestEdgeRequest> Resolve(
        IReadOnlyList<IngestNodeRequest> nodes,
        IReadOnlyList<IngestEdgeRequest> edges) =>
        ResolveWithDiagnostics(nodes, edges).Edges;

    public static EdgeResolutionResult ResolveWithDiagnostics(
        IReadOnlyList<IngestNodeRequest> nodes,
        IReadOnlyList<IngestEdgeRequest> edges)
    {
        var nodesById = nodes
            .GroupBy(n => n.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var typeCandidates = nodes
            .Where(n => n.Type is "Class" or "Interface" or "Enum" or "Struct" or "Delegate")
            .Select(n => new TypeCandidate(n.Id, n.Type, n.Namespace, n.FilePath, n.Name, ShortTypeName(n.Id)))
            .GroupBy(n => (n.Type, n.Name), StringTupleComparer.OrdinalType)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringTupleComparer.OrdinalType);
        var typeCandidatesByName = nodes
            .Where(n => n.Type is "Class" or "Interface" or "Enum" or "Struct" or "Delegate")
            .Select(n => new TypeCandidate(n.Id, n.Type, n.Namespace, n.FilePath, n.Name, ShortTypeName(n.Id)))
            .GroupBy(n => n.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);

        var resolved = new List<IngestEdgeRequest>(edges.Count);
        var diagnostics = new EdgeResolutionDiagnostics();
        var attempted = 0;
        foreach (var edge in edges)
        {
            if (edge.RelationshipType is not ("Uses" or "Implements" or "Inherits"))
            {
                resolved.Add(edge);
                continue;
            }

            attempted++;

            if (nodesById.ContainsKey(edge.TargetId))
            {
                resolved.Add(edge);
                continue;
            }

            if (!nodesById.TryGetValue(edge.SourceId, out var source))
            {
                diagnostics.Add("missing_source");
                continue;
            }

            if (edge.TargetName is null || edge.TargetType is null)
            {
                diagnostics.Add("missing_target_metadata");
                continue;
            }

            if (!typeCandidates.TryGetValue((edge.TargetType, edge.TargetName), out var candidates) &&
                !typeCandidatesByName.TryGetValue(edge.TargetName, out candidates))
            {
                diagnostics.Add("missing_target");
                continue;
            }

            var selected = SelectBestTypeCandidate(source, candidates);
            if (selected is not null)
                resolved.Add(edge with { TargetId = selected.Id });
            else
                diagnostics.Add("ambiguous_target");
        }

        var memberImplementationEdges = BuildMemberImplementationEdges(nodes, resolved);
        resolved.AddRange(memberImplementationEdges);

        var distinct = resolved
            .Where(edge => !string.IsNullOrWhiteSpace(edge.TargetId))
            .DistinctBy(BuildEdgeIdentity, StringComparer.Ordinal)
            .ToList();
        var resolvedCount = distinct.Count(edge => edge.RelationshipType is "Uses" or "Implements" or "Inherits");
        return new EdgeResolutionResult(distinct, attempted, resolvedCount, diagnostics.Snapshot());
    }

    private static string BuildEdgeIdentity(IngestEdgeRequest edge) =>
        edge.RelationshipType is "ReadsConfig" or "BindsConfig"
            ? $"{edge.SourceId}|{edge.TargetId}|{edge.RelationshipType}|{ReadProperty(edge, "accessPattern")}"
            : $"{edge.SourceId}|{edge.TargetId}|{edge.RelationshipType}";

    private static string? ReadProperty(IngestEdgeRequest edge, string key) =>
        edge.Properties is not null && edge.Properties.TryGetValue(key, out var value) ? value : null;

    private static string? ReadProperty(Dictionary<string, string>? properties, string key) =>
        properties is not null && properties.TryGetValue(key, out var value) ? value : null;

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

    private static IEnumerable<IngestEdgeRequest> BuildMemberImplementationEdges(
        IReadOnlyList<IngestNodeRequest> nodes,
        IReadOnlyList<IngestEdgeRequest> resolvedEdges)
    {
        var methodsByDeclaringType = nodes
            .Where(node => node.Type == "Method")
            .Select(node => new
            {
                Node = node,
                DeclaringTypeId = ReadProperty(node.Properties, "declaringTypeId")
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.DeclaringTypeId))
            .GroupBy(item => item.DeclaringTypeId!, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.Node).ToArray(),
                StringComparer.Ordinal);

        foreach (var edge in resolvedEdges.Where(edge => edge.RelationshipType == "Implements").ToArray())
        {
            if (!methodsByDeclaringType.TryGetValue(edge.SourceId, out var implementationMethods)
                || !methodsByDeclaringType.TryGetValue(edge.TargetId, out var interfaceMethods))
            {
                continue;
            }

            foreach (var implementationMethod in implementationMethods)
            {
                var matches = interfaceMethods
                    .Where(interfaceMethod => string.Equals(interfaceMethod.Name, implementationMethod.Name, StringComparison.Ordinal))
                    .ToArray();

                if (matches.Length != 1)
                    continue;

                yield return new IngestEdgeRequest(
                    implementationMethod.Id,
                    matches[0].Id,
                    "Implements");
            }
        }
    }
}
