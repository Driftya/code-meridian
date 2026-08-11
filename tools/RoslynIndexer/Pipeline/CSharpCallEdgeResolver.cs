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
                ReadProperty(n.Properties, "declaringTypeId"),
                ReadProperty(n.Properties, "declaringTypeShortName"),
                ReadProperty(n.Properties, "declaringTypeCanonicalName"),
                ReadBooleanProperty(n.Properties, "hasParamsParameter"),
                ReadBooleanProperty(n.Properties, "isExtensionMethod"),
                ReadProperty(n.Properties, "extensionReceiverType"),
                ReadProperty(n.Properties, "extensionReceiverCanonicalType"),
                TryReadIntProperty(n.Properties, "genericParameterCount") ?? 0,
                string.Equals(ReadProperty(n.Properties, "parameterMetadata"), "exact-syntax", StringComparison.Ordinal)))
            .GroupBy(n => n.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
        var localTypeNames = nodes
            .Where(node => node.Type is "Class" or "Interface" or "Struct" or "Record")
            .Select(node => CSharpTypeIdentityNormalizer.Normalize(node.Name)?.ShortName)
            .Where(name => name is not null)
            .Select(name => name!)
            .ToHashSet(StringComparer.Ordinal);
        var localTypeHierarchy = BuildLocalTypeHierarchy(nodes, edges);
        var localTypeIdsByName = nodes
            .Where(node => node.Type is "Class" or "Interface" or "Struct" or "Record")
            .GroupBy(
                node => CSharpTypeIdentityNormalizer.Normalize(node.Name)?.ShortName ?? node.Name,
                StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(node => node.Id).ToArray(),
                StringComparer.Ordinal);
        var typesWithUnindexedBases = FindTypesWithUnindexedBases(nodes, edges);

        var resolved = new List<IngestEdgeRequest>(edges.Count);
        var outcomes = new RelationshipResolutionCollector("Calls");
        foreach (var edge in edges)
        {
            if (edge.RelationshipType != "Calls")
            {
                resolved.Add(edge);
                continue;
            }

            nodesById.TryGetValue(edge.SourceId, out var source);
            if (edge.CallName is null)
            {
                outcomes.Record(RelationshipResolutionDisposition.Indeterminate, "missing_call_name", source, edge);
                continue;
            }

            if (source is null)
            {
                outcomes.Record(RelationshipResolutionDisposition.Indeterminate, "missing_source", null, edge);
                continue;
            }

            if (edge.ParamCount is null)
            {
                outcomes.Record(RelationshipResolutionDisposition.Indeterminate, "missing_parameter_metadata", source, edge);
                continue;
            }

            var receiverIdentity = CSharpTypeIdentityNormalizer.Normalize(
                ReadProperty(edge, "receiverCanonicalTypeHint") ?? ReadProperty(edge, "receiverTypeHint"));
            var receiverTypeHint = receiverIdentity?.ShortName;
            var receiverCanonicalTypeHint = receiverIdentity?.CanonicalName;
            var receiverKind = ReadProperty(edge, "receiverKind");
            var semanticTargetDeclaringTypeHint = ReadProperty(edge, "semanticTargetDeclaringTypeHint");
            var genericArity = TryReadIntProperty(edge.Properties, "genericArity");
            if (!methodCandidates.TryGetValue(edge.CallName, out var candidates))
            {
                if (IsPossibleExternalBaseMember(source, receiverKind, typesWithUnindexedBases))
                {
                    outcomes.Record(
                        RelationshipResolutionDisposition.Indeterminate,
                        "external_base_member_possible",
                        source,
                        edge);
                    continue;
                }

                var disposition = ClassifyMissingTarget(receiverKind, receiverTypeHint, localTypeNames);
                outcomes.Record(disposition, MissingTargetReason(disposition), source, edge);
                continue;
            }

            var compatibleCandidates = candidates
                .Where(candidate => HasCompatibleArity(
                    candidate,
                    edge.ParamCount.Value,
                    receiverTypeHint,
                    receiverKind,
                    genericArity))
                .ToArray();
            if (compatibleCandidates.Length == 0)
            {
                if (candidates.Any(candidate => !candidate.HasExactParameterMetadata))
                {
                    outcomes.Record(
                        RelationshipResolutionDisposition.Indeterminate,
                        "insufficient_arity_metadata",
                        source,
                        edge);
                    continue;
                }

                var externalExtensionPossible = IsPossibleExternalExtensionCall(
                    edge,
                    receiverKind,
                    receiverTypeHint,
                    receiverCanonicalTypeHint,
                    candidates,
                    localTypeIdsByName,
                    localTypeHierarchy);
                var disposition = externalExtensionPossible
                    || IsKnownExternalReceiver(receiverKind, receiverTypeHint, localTypeNames)
                    ? RelationshipResolutionDisposition.ExternalOrUnindexed
                    : RelationshipResolutionDisposition.UnresolvedLocal;
                outcomes.Record(
                    disposition,
                    externalExtensionPossible
                        ? "external_extension_possible"
                        : disposition == RelationshipResolutionDisposition.ExternalOrUnindexed
                            ? "external_receiver_incompatible_arity"
                            : "local_target_incompatible_arity",
                    source,
                    edge);
                continue;
            }

            if (string.Equals(receiverKind, "Chained", StringComparison.Ordinal))
            {
                var disposition = IsKnownLocalReceiver(receiverTypeHint, localTypeNames)
                    ? RelationshipResolutionDisposition.Indeterminate
                    : RelationshipResolutionDisposition.ExternalOrUnindexed;
                outcomes.Record(
                    disposition,
                    disposition == RelationshipResolutionDisposition.Indeterminate
                        ? "chained_receiver_return_unknown"
                        : "external_chain_root",
                    source,
                    edge);
                continue;
            }

            if (string.Equals(receiverKind, "UnknownMember", StringComparison.Ordinal))
            {
                var testSubject = SelectTestSubjectMatch(source, compatibleCandidates);
                if (testSubject is not null)
                {
                    var resolvedEdge = edge with { TargetId = testSubject.Id };
                    outcomes.RecordResolved(source, resolvedEdge);
                    resolved.Add(resolvedEdge);
                }
                else
                    outcomes.Record(RelationshipResolutionDisposition.Indeterminate, "unknown_member_receiver", source, edge);
                continue;
            }

            var selected = SelectBestCandidate(
                source,
                compatibleCandidates,
                receiverTypeHint,
                receiverCanonicalTypeHint,
                receiverKind,
                semanticTargetDeclaringTypeHint,
                localTypeHierarchy);
            if (selected is not null)
            {
                var resolvedEdge = edge with { TargetId = selected.Id };
                outcomes.RecordResolved(source, resolvedEdge);
                resolved.Add(resolvedEdge);
            }
            else
            {
                if (IsPossibleExternalBaseMember(source, receiverKind, typesWithUnindexedBases))
                {
                    outcomes.Record(
                        RelationshipResolutionDisposition.Indeterminate,
                        "external_base_member_possible",
                        source,
                        edge);
                    continue;
                }

                var externalExtensionPossible = IsPossibleExternalExtensionCall(
                    edge,
                    receiverKind,
                    receiverTypeHint,
                    receiverCanonicalTypeHint,
                    candidates,
                    localTypeIdsByName,
                    localTypeHierarchy);
                var disposition = externalExtensionPossible
                    || IsKnownExternalReceiver(receiverKind, receiverTypeHint, localTypeNames)
                    ? RelationshipResolutionDisposition.ExternalOrUnindexed
                    : RelationshipResolutionDisposition.UnresolvedLocal;
                var reason = externalExtensionPossible
                    ? "external_extension_possible"
                    : disposition == RelationshipResolutionDisposition.ExternalOrUnindexed
                        ? "external_receiver_name_collision"
                    : string.IsNullOrWhiteSpace(receiverTypeHint)
                        ? "missing_receiver_hint"
                        : "ambiguous_local_target";
                outcomes.Record(disposition, reason, source, edge);
            }
        }

        var distinct = resolved
            .DistinctBy(BuildEdgeIdentity, StringComparer.Ordinal)
            .ToList();
        var stats = outcomes.Build();
        return new EdgeResolutionResult(distinct, stats.UniqueResolvedEdges, stats);
    }

    private static RelationshipResolutionDisposition ClassifyMissingTarget(
        string? receiverKind,
        string? receiverTypeHint,
        IReadOnlySet<string> localTypeNames)
    {
        if (string.Equals(receiverKind, "UnknownMember", StringComparison.Ordinal))
            return RelationshipResolutionDisposition.Indeterminate;

        if (string.Equals(receiverKind, "Chained", StringComparison.Ordinal))
        {
            return IsKnownLocalReceiver(receiverTypeHint, localTypeNames)
                ? RelationshipResolutionDisposition.Indeterminate
                : RelationshipResolutionDisposition.ExternalOrUnindexed;
        }

        if (string.Equals(receiverKind, "ThisOrBase", StringComparison.Ordinal)
            || IsKnownLocalReceiver(receiverTypeHint, localTypeNames))
        {
            return RelationshipResolutionDisposition.UnresolvedLocal;
        }

        return RelationshipResolutionDisposition.ExternalOrUnindexed;
    }

    private static string MissingTargetReason(RelationshipResolutionDisposition disposition) =>
        disposition switch
        {
            RelationshipResolutionDisposition.Indeterminate => "unknown_member_receiver",
            RelationshipResolutionDisposition.UnresolvedLocal => "local_target_missing",
            _ => "external_or_unindexed_target"
        };

    private static bool IsKnownExternalReceiver(
        string? receiverKind,
        string? receiverTypeHint,
        IReadOnlySet<string> localTypeNames) =>
        string.Equals(receiverKind, "TypedOrStatic", StringComparison.Ordinal)
        && !string.IsNullOrWhiteSpace(receiverTypeHint)
        && !IsKnownLocalReceiver(receiverTypeHint, localTypeNames);

    private static bool IsKnownLocalReceiver(string? receiverTypeHint, IReadOnlySet<string> localTypeNames) =>
        !string.IsNullOrWhiteSpace(receiverTypeHint)
        && localTypeNames.Contains(receiverTypeHint);

    private static bool IsPossibleExternalExtensionCall(
        IngestEdgeRequest edge,
        string? receiverKind,
        string? receiverTypeHint,
        string? receiverCanonicalTypeHint,
        IReadOnlyList<MethodCandidate> candidates,
        IReadOnlyDictionary<string, string[]> localTypeIdsByName,
        IReadOnlyDictionary<string, IReadOnlySet<string>> localTypeHierarchy)
    {
        if (!string.Equals(receiverKind, "TypedOrStatic", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(receiverTypeHint)
            || !IsInstanceReceiverEvidence(ReadProperty(edge, "receiverEvidenceSource")))
        {
            return false;
        }

        if (candidates.Any(candidate =>
            IsDeclaringTypeMatch(candidate, receiverTypeHint, receiverCanonicalTypeHint)
            || IsExtensionReceiverMatch(candidate, receiverTypeHint, receiverCanonicalTypeHint)))
        {
            return false;
        }

        if (!localTypeIdsByName.TryGetValue(receiverTypeHint, out var receiverTypeIds))
            return true;

        return !candidates.Any(candidate =>
            candidate.DeclaringTypeId is not null
            && receiverTypeIds.Any(receiverTypeId =>
                localTypeHierarchy.TryGetValue(receiverTypeId, out var hierarchy)
                && hierarchy.Contains(candidate.DeclaringTypeId)));
    }

    private static bool IsInstanceReceiverEvidence(string? evidenceSource) =>
        evidenceSource is
            "syntax-lexical-variable" or
            "syntax-parameter" or
            "syntax-member" or
            "syntax-this-member" or
            "syntax-object-creation" or
            "syntax-cast" or
            "syntax-as-cast" or
            "syntax-conditional" or
            "semantic-model-instance";

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
        string? receiverCanonicalTypeHint,
        string? receiverKind,
        string? semanticTargetDeclaringTypeHint,
        IReadOnlyDictionary<string, IReadOnlySet<string>> localTypeHierarchy)
    {
        if (CSharpTypeIdentityNormalizer.Normalize(semanticTargetDeclaringTypeHint) is { } semanticTarget)
        {
            var semanticMatches = candidates
                .Where(candidate => IsDeclaringTypeMatch(
                    candidate,
                    semanticTarget.ShortName,
                    semanticTarget.CanonicalName))
                .ToArray();
            if (semanticMatches.Length == 1)
                return semanticMatches[0];
        }

        if (!string.IsNullOrWhiteSpace(receiverTypeHint))
        {
            var exactReceiverMatches = candidates
                .Where(candidate => IsDeclaringTypeMatch(candidate, receiverTypeHint, receiverCanonicalTypeHint)
                    || IsExtensionReceiverMatch(candidate, receiverTypeHint, receiverCanonicalTypeHint))
                .ToArray();
            if (exactReceiverMatches.Length == 1)
                return exactReceiverMatches[0];

            if (string.Equals(receiverKind, "TypedOrStatic", StringComparison.Ordinal))
                return null;
        }

        var sourceDeclaringTypeId = ReadProperty(source.Properties, "declaringTypeId");
        if (receiverKind is "Unqualified" or "ThisOrBase")
        {
            var sourceDeclaringType = ReadProperty(source.Properties, "declaringTypeShortName");
            var exactDeclaringTypeMatches = candidates
                .Where(candidate => string.Equals(
                    candidate.DeclaringTypeShortName,
                    sourceDeclaringType,
                    StringComparison.Ordinal))
                .ToArray();
            if (exactDeclaringTypeMatches.Length == 1)
                return exactDeclaringTypeMatches[0];
        }

        if (sourceDeclaringTypeId is not null
            && receiverKind is "Unqualified" or "ThisOrBase")
        {
            var relatedTypeIds = localTypeHierarchy.GetValueOrDefault(sourceDeclaringTypeId)
                ?? new HashSet<string>([sourceDeclaringTypeId], StringComparer.Ordinal);
            var sameTypeOrBase = candidates
                .Where(candidate => candidate.DeclaringTypeId is not null
                    && relatedTypeIds.Contains(candidate.DeclaringTypeId))
                .ToArray();
            if (sameTypeOrBase.Length == 1)
                return sameTypeOrBase[0];

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

    private static bool HasCompatibleArity(
        MethodCandidate candidate,
        int argumentCount,
        string? receiverTypeHint,
        string? receiverKind,
        int? genericArity)
    {
        if (genericArity is > 0 && candidate.GenericParameterCount != genericArity.Value)
            return false;

        var receiverAdjustment = UsesExtensionInstanceSyntax(candidate, receiverTypeHint, receiverKind) ? 1 : 0;
        var requiredCount = Math.Max(0, candidate.RequiredParameterCount - receiverAdjustment);
        var totalCount = Math.Max(0, candidate.TotalParameterCount - receiverAdjustment);
        return requiredCount <= argumentCount
            && (candidate.HasParamsParameter || argumentCount <= totalCount);
    }

    private static bool UsesExtensionInstanceSyntax(
        MethodCandidate candidate,
        string? receiverTypeHint,
        string? receiverKind) =>
        candidate.IsExtensionMethod
        && string.Equals(receiverKind, "TypedOrStatic", StringComparison.Ordinal)
        && IsExtensionReceiverMatch(candidate, receiverTypeHint, receiverCanonicalTypeHint: null);

    private static bool IsExtensionReceiverMatch(
        MethodCandidate candidate,
        string? receiverTypeHint,
        string? receiverCanonicalTypeHint) =>
        candidate.IsExtensionMethod
        && !string.IsNullOrWhiteSpace(receiverTypeHint)
        && (HasQualifiedIdentity(receiverCanonicalTypeHint)
            && candidate.ExtensionReceiverCanonicalType is not null
                ? string.Equals(candidate.ExtensionReceiverCanonicalType, receiverCanonicalTypeHint, StringComparison.Ordinal)
                : string.Equals(candidate.ExtensionReceiverType, receiverTypeHint, StringComparison.Ordinal));

    private static bool IsDeclaringTypeMatch(
        MethodCandidate candidate,
        string receiverTypeHint,
        string? receiverCanonicalTypeHint) =>
        HasQualifiedIdentity(receiverCanonicalTypeHint)
        && candidate.DeclaringTypeCanonicalName is not null
            ? string.Equals(candidate.DeclaringTypeCanonicalName, receiverCanonicalTypeHint, StringComparison.Ordinal)
            : string.Equals(candidate.DeclaringTypeShortName, receiverTypeHint, StringComparison.Ordinal);

    private static bool HasQualifiedIdentity(string? typeName) =>
        typeName?.Contains('.', StringComparison.Ordinal) == true
        || typeName?.Contains('+', StringComparison.Ordinal) == true;

    private static bool ReadBooleanProperty(Dictionary<string, string>? properties, string key) =>
        ReadProperty(properties, key) is { } rawValue
        && bool.TryParse(rawValue, out var value)
        && value;

    private static IReadOnlyDictionary<string, IReadOnlySet<string>> BuildLocalTypeHierarchy(
        IReadOnlyList<IngestNodeRequest> nodes,
        IReadOnlyList<IngestEdgeRequest> edges)
    {
        var typeNodes = nodes
            .Where(node => node.Type is "Class" or "Interface" or "Struct" or "Record")
            .ToArray();
        var typeIds = typeNodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        var typeIdsByName = typeNodes
            .GroupBy(node => CSharpTypeIdentityNormalizer.Normalize(node.Name)?.ShortName ?? node.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(node => node.Id).ToArray(), StringComparer.Ordinal);
        var directBases = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var edge in edges.Where(edge => edge.RelationshipType is "Inherits" or "Implements"))
        {
            if (!typeIds.Contains(edge.SourceId))
                continue;

            var targetId = typeIds.Contains(edge.TargetId)
                ? edge.TargetId
                : edge.TargetName is not null
                    && typeIdsByName.TryGetValue(edge.TargetName, out var matches)
                    && matches.Length == 1
                        ? matches[0]
                        : null;
            if (targetId is null)
                continue;

            if (!directBases.TryGetValue(edge.SourceId, out var bases))
            {
                bases = new HashSet<string>(StringComparer.Ordinal);
                directBases[edge.SourceId] = bases;
            }
            bases.Add(targetId);
        }

        return typeIds.ToDictionary(
            typeId => typeId,
            typeId => (IReadOnlySet<string>)CollectTypeClosure(typeId, directBases),
            StringComparer.Ordinal);
    }

    private static HashSet<string> CollectTypeClosure(
        string typeId,
        IReadOnlyDictionary<string, HashSet<string>> directBases)
    {
        var result = new HashSet<string>(StringComparer.Ordinal) { typeId };
        var pending = new Stack<string>();
        pending.Push(typeId);
        while (pending.TryPop(out var current))
        {
            if (!directBases.TryGetValue(current, out var bases))
                continue;

            foreach (var baseId in bases)
            {
                if (result.Add(baseId))
                    pending.Push(baseId);
            }
        }

        return result;
    }

    private static IReadOnlySet<string> FindTypesWithUnindexedBases(
        IReadOnlyList<IngestNodeRequest> nodes,
        IReadOnlyList<IngestEdgeRequest> edges)
    {
        var localTypeIds = nodes
            .Where(node => node.Type is "Class" or "Interface" or "Struct" or "Record")
            .Select(node => node.Id)
            .ToHashSet(StringComparer.Ordinal);
        var localTypeNames = nodes
            .Where(node => node.Type is "Class" or "Interface" or "Struct" or "Record")
            .Select(node => CSharpTypeIdentityNormalizer.Normalize(node.Name)?.ShortName ?? node.Name)
            .ToHashSet(StringComparer.Ordinal);

        return edges
            .Where(edge => edge.RelationshipType == "Inherits"
                && localTypeIds.Contains(edge.SourceId)
                && !localTypeIds.Contains(edge.TargetId)
                && (edge.TargetName is null || !localTypeNames.Contains(edge.TargetName)))
            .Select(edge => edge.SourceId)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static bool IsPossibleExternalBaseMember(
        IngestNodeRequest source,
        string? receiverKind,
        IReadOnlySet<string> typesWithUnindexedBases) =>
        string.Equals(receiverKind, "ThisOrBase", StringComparison.Ordinal)
        && ReadProperty(source.Properties, "declaringTypeId") is { } declaringTypeId
        && typesWithUnindexedBases.Contains(declaringTypeId);

    private static int? TryReadIntProperty(Dictionary<string, string>? properties, string key) =>
        ReadProperty(properties, key) is { } rawValue && int.TryParse(rawValue, out var value)
            ? value
            : null;
}
