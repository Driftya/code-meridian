namespace CodeMeridian.RoslynIndexer.Pipeline;

internal static class CSharpCallEdgeResolver
{
    public static List<IngestEdgeRequest> Resolve(
        IReadOnlyList<IngestNodeRequest> nodes,
        IReadOnlyList<IngestEdgeRequest> edges)
    {
        var nodesById = nodes
            .GroupBy(n => n.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var methodCandidates = nodes
            .Where(n => n.Type.Equals("Method", StringComparison.OrdinalIgnoreCase))
            .Select(n => new MethodCandidate(
                n.Id,
                n.Namespace,
                n.FilePath,
                MethodName(n.Name),
                RequiredParameterCount(n),
                TotalParameterCount(n),
                ReadProperty(n.Properties, "declaringTypeShortName")))
            .GroupBy(n => n.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);

        var resolved = new List<IngestEdgeRequest>(edges.Count);
        foreach (var edge in edges)
        {
            if (edge.RelationshipType != "Calls" || edge.CallName is null)
            {
                resolved.Add(edge);
                continue;
            }

            if (!nodesById.TryGetValue(edge.SourceId, out var source))
                continue;

            if (edge.ParamCount is null)
                continue;

            if (!methodCandidates.TryGetValue(edge.CallName, out var candidates))
                continue;

            var compatibleCandidates = candidates
                .Where(candidate => candidate.RequiredParameterCount <= edge.ParamCount.Value
                    && edge.ParamCount.Value <= candidate.TotalParameterCount)
                .ToArray();
            if (compatibleCandidates.Length == 0)
                continue;

            var selected = SelectBestCandidate(source, compatibleCandidates, ReadProperty(edge, "receiverTypeHint"));
            if (selected is not null)
                resolved.Add(edge with { TargetId = selected.Id });
        }

        return resolved
            .DistinctBy(BuildEdgeIdentity, StringComparer.Ordinal)
            .ToList();
    }

    private static string BuildEdgeIdentity(IngestEdgeRequest edge) =>
        edge.RelationshipType is "ReadsConfig" or "BindsConfig"
            ? $"{edge.SourceId}|{edge.TargetId}|{edge.RelationshipType}|{ReadProperty(edge, "accessPattern")}"
            : $"{edge.SourceId}|{edge.TargetId}|{edge.RelationshipType}";

    private static string? ReadProperty(IngestEdgeRequest edge, string key) =>
        edge.Properties is not null && edge.Properties.TryGetValue(key, out var value) ? value : null;

    private static string? ReadProperty(Dictionary<string, string>? properties, string key) =>
        properties is not null && properties.TryGetValue(key, out var value) ? value : null;

    private static MethodCandidate? SelectBestCandidate(
        IngestNodeRequest source,
        IReadOnlyList<MethodCandidate> candidates,
        string? receiverTypeHint)
    {
        if (!string.IsNullOrWhiteSpace(receiverTypeHint))
        {
            var exactReceiverMatches = candidates
                .Where(candidate => string.Equals(candidate.DeclaringTypeShortName, receiverTypeHint, StringComparison.Ordinal))
                .ToArray();
            if (exactReceiverMatches.Length == 1)
                return exactReceiverMatches[0];
        }

        if (candidates.Count == 1)
            return candidates[0];

        var sameFile = candidates
            .Where(candidate => string.Equals(candidate.FilePath, source.FilePath, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (sameFile.Length == 1)
            return sameFile[0];

        var sameNamespace = candidates
            .Where(candidate => string.Equals(candidate.Namespace, source.Namespace, StringComparison.Ordinal))
            .ToArray();
        return sameNamespace.Length == 1 ? sameNamespace[0] : null;
    }

    private static string MethodName(string signature)
    {
        var openParen = signature.IndexOf('(');
        return openParen > 0 ? signature[..openParen] : signature;
    }

    private static int ParameterCount(string signature)
    {
        var openParen = signature.IndexOf('(');
        var closeParen = signature.LastIndexOf(')');
        if (openParen < 0 || closeParen <= openParen + 1)
            return 0;

        return signature[(openParen + 1)..closeParen]
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Length;
    }

    private static int TotalParameterCount(IngestNodeRequest node) =>
        TryReadIntProperty(node.Properties, "totalParameterCount") ?? ParameterCount(node.Name);

    private static int RequiredParameterCount(IngestNodeRequest node) =>
        TryReadIntProperty(node.Properties, "requiredParameterCount") ?? TotalParameterCount(node);

    private static int? TryReadIntProperty(Dictionary<string, string>? properties, string key) =>
        ReadProperty(properties, key) is { } rawValue && int.TryParse(rawValue, out var value)
            ? value
            : null;
}
