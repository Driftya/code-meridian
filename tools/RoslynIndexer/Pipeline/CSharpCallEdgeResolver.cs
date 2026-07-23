namespace CodeMeridian.RoslynIndexer.Pipeline;

internal static class CSharpCallEdgeResolver
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
        var diagnostics = new EdgeResolutionDiagnostics();
        var attempted = 0;
        foreach (var edge in edges)
        {
            if (edge.RelationshipType != "Calls" || edge.CallName is null)
            {
                resolved.Add(edge);
                continue;
            }

            attempted++;

            if (!nodesById.TryGetValue(edge.SourceId, out var source))
            {
                diagnostics.Add("missing_source");
                continue;
            }

            if (edge.ParamCount is null)
            {
                diagnostics.Add("missing_parameter_metadata");
                continue;
            }

            if (!methodCandidates.TryGetValue(edge.CallName, out var candidates))
            {
                diagnostics.Add("missing_target");
                continue;
            }

            var compatibleCandidates = candidates
                .Where(candidate => candidate.RequiredParameterCount <= edge.ParamCount.Value
                    && edge.ParamCount.Value <= candidate.TotalParameterCount)
                .ToArray();
            if (compatibleCandidates.Length == 0)
            {
                diagnostics.Add("missing_target");
                continue;
            }

            var receiverTypeHint = ReadProperty(edge, "receiverTypeHint");
            var receiverKind = ReadProperty(edge, "receiverKind");
            if (string.Equals(receiverKind, "UnknownMember", StringComparison.Ordinal))
            {
                diagnostics.Add("unknown_member_receiver");
                continue;
            }

            var selected = SelectBestCandidate(source, compatibleCandidates, receiverTypeHint, receiverKind);
            if (selected is not null)
                resolved.Add(edge with { TargetId = selected.Id });
            else
                diagnostics.Add(string.IsNullOrWhiteSpace(receiverTypeHint)
                    ? "missing_receiver_hint"
                    : "ambiguous_target");
        }

        var distinct = resolved
            .DistinctBy(BuildEdgeIdentity, StringComparer.Ordinal)
            .ToList();
        var resolvedCount = distinct.Count(edge => edge.RelationshipType == "Calls");
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

    private static MethodCandidate? SelectBestCandidate(
        IngestNodeRequest source,
        IReadOnlyList<MethodCandidate> candidates,
        string? receiverTypeHint,
        string? receiverKind)
    {
        if (!string.IsNullOrWhiteSpace(receiverTypeHint))
        {
            var exactReceiverMatches = candidates
                .Where(candidate => string.Equals(candidate.DeclaringTypeShortName, receiverTypeHint, StringComparison.Ordinal))
                .ToArray();
            if (exactReceiverMatches.Length == 1)
                return exactReceiverMatches[0];

            if (string.Equals(receiverKind, "TypedOrStatic", StringComparison.Ordinal))
                return null;
        }

        if (candidates.Count == 1)
            return candidates[0];

        var sameFile = candidates
            .Where(candidate => string.Equals(candidate.FilePath, source.FilePath, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (sameFile.Length == 1)
            return sameFile[0];

        var testSubjectMatch = SelectTestSubjectMatch(source, candidates);
        if (testSubjectMatch is not null)
            return testSubjectMatch;

        var sameNamespace = candidates
            .Where(candidate => string.Equals(candidate.Namespace, source.Namespace, StringComparison.Ordinal))
            .ToArray();
        return sameNamespace.Length == 1 ? sameNamespace[0] : null;
    }

    private static MethodCandidate? SelectTestSubjectMatch(
        IngestNodeRequest source,
        IReadOnlyList<MethodCandidate> candidates)
    {
        var declaringType = ReadProperty(source.Properties, "declaringTypeShortName");
        var subjectName = RemoveTestTypeSuffix(declaringType);
        if (subjectName is null)
            return null;

        var matches = candidates
            .Where(candidate => candidate.DeclaringTypeShortName is { Length: > 0 } candidateType
                && IsTypeNamePrefix(subjectName, candidateType))
            .ToArray();
        if (matches.Length == 0)
            return null;

        var longestTypeName = matches.Max(candidate => candidate.DeclaringTypeShortName!.Length);
        var strongestMatches = matches
            .Where(candidate => candidate.DeclaringTypeShortName!.Length == longestTypeName)
            .ToArray();
        return strongestMatches.Length == 1 ? strongestMatches[0] : null;
    }

    private static string? RemoveTestTypeSuffix(string? typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            return null;

        foreach (var suffix in new[] { "Tests", "Specs", "Test", "Spec" })
        {
            if (typeName.EndsWith(suffix, StringComparison.Ordinal) && typeName.Length > suffix.Length)
                return typeName[..^suffix.Length];
        }

        return null;
    }

    private static bool IsTypeNamePrefix(string subjectName, string candidateType) =>
        subjectName.StartsWith(candidateType, StringComparison.Ordinal)
        && (subjectName.Length == candidateType.Length
            || char.IsUpper(subjectName[candidateType.Length])
            || char.IsDigit(subjectName[candidateType.Length])
            || subjectName[candidateType.Length] == '_');

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
