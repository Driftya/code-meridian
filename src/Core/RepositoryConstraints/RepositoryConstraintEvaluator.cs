using CodeMeridian.Core.Projects;

namespace CodeMeridian.Core.RepositoryConstraints;

// Combines inventory placement and structural reference facts without semantic code prerequisites.
public static class RepositoryConstraintEvaluator
{
    public static RepositoryConstraintResult Evaluate(ProjectProfileSnapshot snapshot, string? targetPath = null,
        int findingLimit = 100, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        findingLimit = Math.Clamp(findingLimit, 1, 1000);
        var error = snapshot.Validate();
        if (error is not null || snapshot.ConstraintEvidence is null)
            return new(snapshot.Generation, "incomplete", "unknown", null, [],
                [new("repositoryConstraints", "invalid_constraint_evidence", error ?? "Bound policy evidence is required.")]);
        var evidence = snapshot.ConstraintEvidence;
        var policy = evidence.Policy;
        var inventory = new RepositoryConstraintInventory(snapshot.Generation, snapshot.Profile.DiscoveryState,
            evidence.CaseSensitive!.Value, snapshot.Files, evidence.ExcludedPaths) { Project = snapshot.Profile.Project };
        var placement = RepositoryPlacementEvaluator.Evaluate(policy, inventory, targetPath, findingLimit, cancellationToken);
        if (placement.Status == "invalid" || placement.Diagnostics.Count > 0) return placement;
        var comparer = inventory.CaseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
        var files = snapshot.Files.ToDictionary(file => file.Path, comparer);
        var excluded = new HashSet<string>(evidence.ExcludedPaths, comparer);
        var target = targetPath is null ? null : RepositoryConstraintPath.Normalize(targetPath);
        var selected = snapshot.Files.Where(file => target is null || comparer.Equals(target, file.Path))
            .OrderBy(file => file.Path, StringComparer.Ordinal).ToArray();
        var documents = snapshot.MarkdownEvidence?.Documents.ToDictionary(document => document.SourcePath, comparer)
            ?? new Dictionary<string, MarkdownReferenceDocument>(comparer);
        var groups = policy.Groups.ToDictionary(group => group.Id, StringComparer.Ordinal);
        var rules = placement.Rules.Where(rule => rule.Family == "placement").ToList();
        var diagnostics = new List<RepositoryConstraintDiagnostic>();
        foreach (var rule in policy.ReferenceRules)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var group = groups[rule.SourceGroup];
            var matchers = group.Paths.Select(pattern => RepositoryConstraintPath.Matcher(pattern, inventory.CaseSensitive)).ToArray();
            var sources = selected.Where(file => (group.FileKinds.Count == 0 || group.FileKinds.Contains(file.Kind, StringComparer.Ordinal))
                && matchers.Any(matcher => matcher.IsMatch(file.Path))).ToArray();
            var complete = inventory.DiscoveryState == "complete" && placement.TargetStatus is null or "observed";
            var findings = new List<RepositoryConstraintFinding>();
            var count = 0;
            var evaluated = 0;
            foreach (var source in sources)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!documents.TryGetValue(source.Path, out var document))
                {
                    complete = false;
                    if (diagnostics.Count < 8) diagnostics.Add(new(source.Path, "missing_reference_evidence", "No current structural Markdown facts were published for this source."));
                    continue;
                }
                var result = MarkdownReferenceEvaluator.EvaluateValidated(policy with { ReferenceRules = [rule] },
                    inventory, document, files, excluded, Math.Max(1, findingLimit - findings.Count), cancellationToken).Result;
                var outcome = result.Rules.Single();
                complete &= outcome.Coverage == "complete";
                evaluated += outcome.EvaluatedFiles;
                count += outcome.FindingCount;
                findings.AddRange(outcome.Findings.Take(Math.Max(0, findingLimit - findings.Count)));
                diagnostics.AddRange(result.Diagnostics.Take(Math.Max(0, 8 - diagnostics.Count)));
            }
            rules.Add(new(rule.Id, "markdown_reference", rule.Severity,
                !complete ? "incomplete" : sources.Length == 0 ? "not_applicable" : "evaluated",
                complete ? "complete" : "partial", rule.Reason, ["repository_inventory", "markdown_references"],
                evaluated, count, count > findings.Count, findings));
        }
        // One response budget across every rule, with uncapped totals retained.
        var remaining = Math.Clamp(findingLimit, 1, 1000);
        var bounded = rules.Select(rule =>
        {
            var findings = rule.Findings.Take(remaining).ToArray();
            remaining -= findings.Length;
            return rule with { Findings = findings, FindingsTruncated = rule.FindingCount > findings.Length };
        }).ToArray();
        var allComplete = placement.TargetStatus is null or "observed" && inventory.DiscoveryState == "complete"
            && bounded.All(rule => rule.Coverage == "complete");
        return new(snapshot.Generation, allComplete ? "evaluated" : "incomplete", allComplete ? "complete" : "partial",
            placement.TargetStatus, bounded, diagnostics);
    }
}
