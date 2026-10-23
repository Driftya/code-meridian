using CodeMeridian.Core.Projects;
using CodeMeridian.Core.RepositoryConstraints;

namespace CodeMeridian.Application.RepositoryConstraints;

public sealed class RepositoryConstraintQueryService(IProjectProfileRepository repository)
{
    public async Task<RepositoryConstraintQueryResult> FindAsync(string projectContext, string? targetPath = null,
        int findingLimit = 100, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(projectContext) || projectContext.Length > 200
            || projectContext != projectContext.Trim() || projectContext.Any(char.IsControl))
            throw new ArgumentException("A nonempty project name of at most 200 characters is required.", nameof(projectContext));
        if (targetPath is not null) targetPath = RepositoryConstraintPath.Normalize(targetPath);
        if (findingLimit is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(findingLimit), "Use a finding limit between 1 and 1000.");

        var stored = await repository.GetRepositoryConstraintsAsync(projectContext, cancellationToken);
        RepositoryConstraintQueryResult Unavailable(string status, string code, string reason, long? latest = null) => new(
            projectContext, targetPath, stored?.Generation, latest ?? stored?.LatestGeneration,
            status, "unknown", null, null, null, [new("repositoryConstraints", code, reason)]);
        if (stored is null) return Unavailable("incomplete", "missing_inventory", "Publish a complete inventory with explicit constraint policy and case/exclusion evidence before querying constraints.");
        if (stored.Profile.Project != projectContext || stored.Generation <= 0 || stored.LatestGeneration < stored.Generation)
            return Unavailable("incomplete", "invalid_inventory", "Stored inventory identity or generation is invalid.");
        if (stored.LatestGeneration != stored.Generation)
            return Unavailable("stale", "stale_inventory", "A newer generation is reserved; retained evidence cannot validate current repository constraints.");
        if (stored.Evidence is not { } evidence)
            return Unavailable("incomplete", "missing_constraint_evidence", "This inventory has no bound constraint policy, filesystem case semantics, or exclusion evidence. No policy or host defaults were inferred.");
        var snapshot = new ProjectProfileSnapshot(stored.Generation, stored.Profile with { EvidenceSource = "local_discovery" }, stored.Files)
            { ConstraintEvidence = evidence };
        if (snapshot.Validate() is { } error)
            return Unavailable("invalid", "invalid_constraint_evidence", error);

        var evaluation = RepositoryPlacementEvaluator.Evaluate(evidence.Policy,
            new(stored.Generation, stored.Profile.DiscoveryState, evidence.CaseSensitive!.Value, stored.Files, evidence.ExcludedPaths)
                { Project = projectContext }, targetPath, findingLimit, cancellationToken);
        // The public limit bounds the entire response, while preserving per-rule totals.
        var remaining = findingLimit;
        var rules = evaluation.Rules.Select(rule =>
        {
            var findings = rule.Findings.Take(remaining).ToArray();
            remaining -= findings.Length;
            return rule with { Findings = findings, FindingsTruncated = rule.FindingCount > findings.Length };
        }).ToArray();
        evaluation = evaluation with { Rules = rules };

        var current = await repository.GetAsync(projectContext, cancellationToken: cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (current is null || current.Generation != stored.Generation || current.LatestGeneration != stored.Generation
            || current.Profile.Project != projectContext
            || current.Profile.SourceFingerprint != stored.Profile.SourceFingerprint
            || current.Profile.ConfigurationFingerprint != stored.Profile.ConfigurationFingerprint)
            return Unavailable("stale", "generation_changed", "Evidence changed during evaluation; findings were discarded. Retry after publication completes.", current?.LatestGeneration);
        return new(projectContext, targetPath, stored.Generation, stored.LatestGeneration,
            evaluation.Status, evaluation.Coverage, evidence.PolicyPath, evidence.PolicyFingerprint, evaluation, []);
    }
}
