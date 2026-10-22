namespace CodeMeridian.Core.RepositoryConstraints;

public sealed record RepositoryConstraintPolicy(
    string ContractVersion,
    IReadOnlyList<RepositoryFileGroup> Groups,
    IReadOnlyList<RepositoryPlacementRule> PlacementRules,
    IReadOnlyList<MarkdownReferenceRule> ReferenceRules)
{
    public IReadOnlyList<RepositoryPlacementRule> PlacementRules { get; init; } = PlacementRules ?? [];
    public IReadOnlyList<MarkdownReferenceRule> ReferenceRules { get; init; } = ReferenceRules ?? [];

    public IReadOnlyList<RepositoryConstraintDiagnostic> Validate()
    {
        var diagnostics = new List<RepositoryConstraintDiagnostic>();
        void Error(string location, string code, string reason) => diagnostics.Add(new(location, code, reason));
        if (ContractVersion != "1.0") Error("contractVersion", "unsupported_version", "Repository constraints require contract version 1.0.");
        if (Groups is null || PlacementRules is null || ReferenceRules is null
            || Groups.Count > 100 || PlacementRules.Count + ReferenceRules.Count > 100)
        {
            Error("repositoryConstraints", "invalid_collections", "Non-null collections with at most 100 groups and 100 rules are required.");
            return diagnostics;
        }
        var groups = new HashSet<string>(StringComparer.Ordinal);
        if (PlacementRules.Count + ReferenceRules.Count == 0)
            Error("repositoryConstraints", "empty_policy", "At least one explicit placement or reference rule is required.");
        for (var index = 0; index < Groups.Count; index++)
        {
            var group = Groups[index];
            var location = $"groups:{index}";
            if (group is null) { Error(location, "invalid_group", "A group is required."); continue; }
            if (!Text(group.Id, 100) || !groups.Add(group.Id)) Error(location, "invalid_id", "Group IDs must be nonempty and unique (case-sensitive).");
            Patterns(group.Paths, location + ":paths", required: true);
            if (group.FileKinds is null || group.FileKinds.Count > 32 || group.FileKinds.Any(kind => !Text(kind, 64)))
                Error(location + ":fileKinds", "invalid_kinds", "File kinds must be a bounded, non-null list of observed kind names.");
        }
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < PlacementRules.Count; index++)
        {
            var rule = PlacementRules[index];
            var location = $"placementRules:{index}";
            if (rule is null) { Error(location, "invalid_rule", "A placement rule is required."); continue; }
            Common(rule.Id, rule.Group, rule.Reason, rule.Severity, rule.Exceptions, location);
            Patterns(rule.AllowedPaths, location + ":allowedPaths", required: false);
            Patterns(rule.DeniedPaths, location + ":deniedPaths", required: false);
            if (rule.AllowedPaths?.Count == 0 && rule.DeniedPaths?.Count == 0)
                Error(location, "empty_rule", "A placement rule must allow or deny at least one path pattern.");
        }
        for (var index = 0; index < ReferenceRules.Count; index++)
        {
            var rule = ReferenceRules[index];
            var location = $"referenceRules:{index}";
            if (rule is null) { Error(location, "invalid_rule", "A reference rule is required."); continue; }
            Common(rule.Id, rule.SourceGroup, rule.Reason, rule.Severity, rule.Exceptions, location);
            if (rule.AllowedTargetGroups is null || rule.AllowedTargetGroups.Count > 100
                || rule.AllowedTargetGroups.Any(group => group is null || !groups.Contains(group)))
                Error(location + ":allowedTargetGroups", "unknown_group", "Target groups must name declared groups.");
            if (rule.ReferenceKinds is null || rule.ReferenceKinds.Count == 0 || rule.ReferenceKinds.Count > 2
                || rule.ReferenceKinds.Any(kind => kind is not ("link" or "image")))
                Error(location + ":referenceKinds", "unsupported_reference_kind", "Reference kinds must be link and/or image.");
            if (!rule.RequireLocalResolution && rule.AllowedTargetGroups?.Count == 0)
                Error(location, "empty_rule", "A reference rule must require resolution or restrict target groups.");
        }
        return diagnostics;

        void Patterns(IReadOnlyList<string>? patterns, string location, bool required)
        {
            if (patterns is null || patterns.Count > 32 || (required && patterns.Count == 0)
                || patterns.Any(pattern => !RepositoryConstraintPath.ValidPattern(pattern)))
                Error(location, "invalid_pattern", "Use at most 32 relative glob patterns with *, ?, and whole-segment **; traversal and custom syntax are unsupported.");
        }

        void Common(string id, string group, string reason, string severity,
            IReadOnlyList<RepositoryConstraintException>? exceptions, string location)
        {
            if (!Text(id, 100) || !ids.Add(id)) Error(location, "invalid_id", "Rule IDs must be nonempty and unique across families.");
            if (group is null || !groups.Contains(group)) Error(location, "unknown_group", "The source group must name a declared group.");
            if (!Text(reason, 512)) Error(location, "invalid_reason", "A bounded rule reason is required.");
            if (severity is not ("info" or "warning" or "error")) Error(location, "invalid_severity", "Severity must be info, warning, or error.");
            if (exceptions is null || exceptions.Count > 32 || exceptions.Any(exception => exception is null
                || !RepositoryConstraintPath.ValidPattern(exception.Path) || !Text(exception.Reason, 512)))
                Error(location + ":exceptions", "invalid_exception", "Exceptions require bounded path patterns and explicit reasons.");
        }
    }

    private static bool Text(string? value, int length) => !string.IsNullOrWhiteSpace(value)
        && value.Length <= length && value == value.Trim() && !value.Any(char.IsControl);
}
