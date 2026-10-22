using System.Text.RegularExpressions;
using CodeMeridian.Core.Projects;

namespace CodeMeridian.Core.RepositoryConstraints;

public static class RepositoryPlacementEvaluator
{
    public static RepositoryConstraintResult Evaluate(RepositoryConstraintPolicy policy,
        RepositoryConstraintInventory inventory, string? targetPath = null, int findingLimit = 100,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var diagnostics = policy.Validate();
        if (diagnostics.Count > 0) return new(inventory.Generation, "invalid", "none", null, [], diagnostics);
        var comparer = inventory.CaseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
        var files = new Dictionary<string, ProjectInventoryFile>(comparer);
        var excluded = new HashSet<string>(comparer);
        bool IsExcluded(string path)
        {
            while (true)
            {
                if (excluded.Contains(path)) return true;
                var separator = path.LastIndexOf('/');
                if (separator < 0) return false;
                path = path[..separator];
            }
        }
        string? target = null;
        try
        {
            if (inventory.Generation <= 0 || inventory.DiscoveryState is not ("complete" or "partial" or "unknown")
                || inventory.Files is null || inventory.ExcludedPaths is null || inventory.Files.Count > 100000 || inventory.ExcludedPaths.Count > 100000)
                throw new ArgumentException("A positive generation and bounded inventory with an explicit discovery state are required.");
            foreach (var file in inventory.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (file is null || string.IsNullOrWhiteSpace(file.Kind)) throw new ArgumentException("Inventory file kinds are required.");
                var path = RepositoryConstraintPath.Normalize(file.Path);
                if (!files.TryAdd(path, file with { Path = path })) throw new ArgumentException("Inventory paths collide under the declared filesystem case semantics.");
            }
            foreach (var path in inventory.ExcludedPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                excluded.Add(RepositoryConstraintPath.Normalize(path));
            }
            if (files.Keys.Any(IsExcluded))
                throw new ArgumentException("Inventory files cannot also be excluded.");
            if (targetPath is not null) target = RepositoryConstraintPath.Normalize(targetPath);
        }
        catch (ArgumentException exception)
        {
            return new(inventory.Generation, "incomplete", "unknown", "unobserved", [],
                [new("inventory", "invalid_inventory_or_target", exception.Message)]);
        }

        var complete = inventory.DiscoveryState == "complete";
        var targetStatus = target is null ? null : files.ContainsKey(target) ? "observed"
            : IsExcluded(target) ? "excluded"
            : complete ? "missing" : "unobserved";
        var selected = (target is null ? files.Values : files.Values.Where(file => comparer.Equals(file.Path, target)))
            .OrderBy(file => file.Path, StringComparer.Ordinal).ToArray();
        var canEvaluate = targetStatus is null or "observed";
        var fullCoverage = complete && canEvaluate;
        var matchers = new Dictionary<string, Regex>(StringComparer.Ordinal);
        bool Match(string pattern, string path)
        {
            if (!matchers.TryGetValue(pattern, out var matcher))
                matchers[pattern] = matcher = RepositoryConstraintPath.Matcher(pattern, inventory.CaseSensitive);
            return matcher.IsMatch(path);
        }
        bool InGroup(RepositoryFileGroup group, ProjectInventoryFile file) =>
            (group.FileKinds.Count == 0 || group.FileKinds.Contains(file.Kind, StringComparer.Ordinal))
            && group.Paths.Any(pattern => Match(pattern, file.Path));
        var groups = policy.Groups.ToDictionary(group => group.Id, StringComparer.Ordinal);
        var outcomes = new List<RepositoryConstraintRuleOutcome>();
        var limit = Math.Clamp(findingLimit, 1, 1000);
        foreach (var rule in policy.PlacementRules)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var findings = new List<RepositoryConstraintFinding>();
            var evaluated = 0;
            var findingCount = 0;
            foreach (var file in selected)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!InGroup(groups[rule.Group], file)) continue;
                evaluated++;
                var exception = rule.Exceptions.FirstOrDefault(item => Match(item.Path, file.Path));
                string? status = null;
                string? reason = null;
                if (exception is not null) { status = "excepted"; reason = exception.Reason; }
                else if (rule.DeniedPaths.Any(pattern => Match(pattern, file.Path))) { status = "forbidden"; reason = rule.Reason; }
                else if (rule.AllowedPaths.Count > 0 && !rule.AllowedPaths.Any(pattern => Match(pattern, file.Path))) { status = "misplaced"; reason = rule.Reason; }
                if (status is null) continue;
                findingCount++;
                if (findings.Count < limit) findings.Add(new(file.Path, status, reason!));
            }
            outcomes.Add(new(rule.Id, "placement", rule.Severity,
                !fullCoverage ? "incomplete" : evaluated == 0 ? "not_applicable" : "evaluated",
                fullCoverage ? "complete" : "partial",
                canEvaluate ? fullCoverage ? rule.Reason : "Discovery is incomplete; observed-file findings do not establish clean repository coverage."
                    : $"The target is {targetStatus}; placement was not evaluated.",
                ["repository_inventory"], evaluated, findingCount, findingCount > findings.Count, findings));
        }
        foreach (var rule in policy.ReferenceRules)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var applicable = selected.Any(file => InGroup(groups[rule.SourceGroup], file));
            outcomes.Add(new(rule.Id, "markdown_reference", rule.Severity,
                !fullCoverage ? "incomplete" : applicable ? "unsupported" : "not_applicable",
                fullCoverage && !applicable ? "complete" : "unknown",
                "Markdown-reference evidence is not evaluated by the inventory-only placement evaluator.",
                ["repository_inventory", "markdown_references"], 0, 0, false, []));
        }
        return new(inventory.Generation, !fullCoverage ? "incomplete"
                : outcomes.Any(outcome => outcome.Status == "unsupported") ? "unsupported" : "evaluated",
            !fullCoverage ? "partial" : outcomes.Any(outcome => outcome.Status == "unsupported") ? "unknown" : "complete",
            targetStatus, outcomes, []);
    }
}
