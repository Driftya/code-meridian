using System.Text.RegularExpressions;
using CodeMeridian.Core.Projects;

namespace CodeMeridian.Core.RepositoryConstraints;

// Evaluates one file's parsed facts. Publication/freshness and project aggregation belong to the caller.
public static class MarkdownReferenceEvaluator
{
    public static MarkdownReferenceEvaluation Evaluate(RepositoryConstraintPolicy policy,
        RepositoryConstraintInventory inventory, MarkdownReferenceDocument document, int findingLimit = 100,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var diagnostics = policy.Validate();
        MarkdownReferenceEvaluation Failure(string status, string code, string reason) => new(
            new(inventory.Generation, status, "unknown", "unobserved", [], [new(document.SourcePath, code, reason)]), []);
        if (diagnostics.Count > 0) return new(new(inventory.Generation, "invalid", "none", null, [], diagnostics), []);
        if (document.ContractVersion != "1.0" || document.Generation != inventory.Generation
            || inventory.Generation <= 0 || string.IsNullOrWhiteSpace(inventory.Project) || inventory.Project.Length > 200
            || inventory.Project != inventory.Project.Trim() || inventory.Project.Any(char.IsControl) || document.Project != inventory.Project
            || document.Status is not ("complete" or "incomplete")
            || document.FileId != $"{document.Project}::doc::{document.SourcePath}" || !ProjectAnalyzerFingerprint.ValidHash(document.TextFingerprint)
            || !ProjectAnalyzerFingerprint.ValidHash(document.ParserFingerprint)
            || document.References is null || document.References.Count > 1000
            || document.Diagnostics is null || document.Diagnostics.Count > 8)
            return Failure("incomplete", "invalid_reference_evidence", "Reference evidence must identify the same project/generation with bounded versioned parser and text evidence.");
        if (inventory.DiscoveryState is not ("complete" or "partial" or "unknown")
            || inventory.Files is null || inventory.Files.Count > 100000 || inventory.ExcludedPaths is null || inventory.ExcludedPaths.Count > 100000)
            return Failure("incomplete", "invalid_inventory", "A bounded inventory with explicit discovery state is required.");
        var comparer = inventory.CaseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
        var files = new Dictionary<string, ProjectInventoryFile>(comparer);
        var excluded = new HashSet<string>(comparer);
        string sourcePath;
        try
        {
            sourcePath = RepositoryConstraintPath.Normalize(document.SourcePath);
            if (sourcePath != document.SourcePath) throw new ArgumentException("Reference source paths must be canonical.");
            foreach (var file in inventory.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (file is null || string.IsNullOrWhiteSpace(file.Kind)) throw new ArgumentException("Inventory file kinds are required.");
                var path = RepositoryConstraintPath.Normalize(file.Path);
                if (!files.TryAdd(path, file with { Path = path })) throw new ArgumentException("Inventory paths collide under the declared case semantics.");
            }
            foreach (var path in inventory.ExcludedPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                excluded.Add(RepositoryConstraintPath.Normalize(path));
            }
            if (files.Keys.Any(IsExcluded)) throw new ArgumentException("Accepted files cannot also be excluded.");
            foreach (var fact in document.References)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (fact is null || fact.Kind is not ("link" or "image") || fact.RawTarget is null || fact.RawTarget.Length > 2048
                    || fact.Target is null || fact.Target.Length > 2048 || !ValidSpan(fact.SourceSpan) || !ValidSpan(fact.TargetSpan))
                    throw new ArgumentException("Reference facts require bounded targets and source spans.");
                var target = MarkdownReferenceTarget.Normalize(fact.Target, sourcePath);
                if (fact.TargetKind != target.Kind || fact.NormalizedPath != target.Path || fact.Fragment != target.Fragment)
                    throw new ArgumentException("Reference target normalization does not match its source fact.");
            }
        }
        catch (ArgumentException exception) { return Failure("incomplete", "invalid_reference_inventory", exception.Message); }
        return EvaluateValidated(policy, inventory, document, files, excluded, findingLimit, cancellationToken);

        bool IsExcluded(string path) => Excluded(path, excluded);
    }

    // The project evaluator validates the complete envelope once and shares the inventory index.
    internal static MarkdownReferenceEvaluation EvaluateValidated(RepositoryConstraintPolicy policy,
        RepositoryConstraintInventory inventory, MarkdownReferenceDocument document,
        IReadOnlyDictionary<string, ProjectInventoryFile> files, HashSet<string> excluded,
        int findingLimit, CancellationToken cancellationToken)
    {
        var sourcePath = document.SourcePath;
        if (!files.TryGetValue(sourcePath, out var source))
            return new(new(inventory.Generation, "incomplete", "unknown", IsExcluded(sourcePath) ? "excluded"
                : inventory.DiscoveryState == "complete" ? "missing" : "unobserved", [],
                [new(sourcePath, "source_not_observed", "Only accepted source files can contribute reference evidence.")]), []);

        var resolutions = document.References.Select(reference =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var status = reference.TargetKind == "local"
                ? files.ContainsKey(reference.NormalizedPath!) ? "resolved" : IsExcluded(reference.NormalizedPath!) ? "excluded"
                    : inventory.DiscoveryState == "complete" ? "missing" : "unobserved"
                : reference.TargetKind;
            return new MarkdownReferenceResolution(reference, status,
                reference.Fragment is not null ? "unverified" : "none", status switch
                {
                    "resolved" => "Target exists in the accepted inventory; content and fragments are not validated.",
                    "external" => "External URL recorded without fetching.",
                    "fragment_only" => "Same-document reference recorded; renderer-specific anchors are unverified.",
                    "excluded" => "Target is explicitly excluded from inventory.",
                    "unobserved" => "Incomplete discovery cannot establish whether the target exists.",
                    "missing" => "Target is absent from complete accepted inventory.",
                    "outside_repository" => "Target escapes the repository; no filesystem traversal occurred.",
                    _ => "Target syntax is unsupported; no resolution was attempted."
                });
        }).ToArray();
        var matchers = new Dictionary<string, Regex>(StringComparer.Ordinal);
        bool Match(string pattern, string path)
        {
            if (!matchers.TryGetValue(pattern, out var matcher)) matchers[pattern] = matcher = RepositoryConstraintPath.Matcher(pattern, inventory.CaseSensitive);
            return matcher.IsMatch(path);
        }
        bool InGroup(RepositoryFileGroup group, ProjectInventoryFile file) =>
            (group.FileKinds.Count == 0 || group.FileKinds.Contains(file.Kind, StringComparer.Ordinal))
            && group.Paths.Any(pattern => Match(pattern, file.Path));
        var groups = policy.Groups.ToDictionary(group => group.Id, StringComparer.Ordinal);
        var outcomes = new List<RepositoryConstraintRuleOutcome>();
        var complete = inventory.DiscoveryState == "complete" && document.Status == "complete" && document.Diagnostics.Count == 0;
        foreach (var rule in policy.ReferenceRules)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var applicable = InGroup(groups[rule.SourceGroup], source);
            var findings = new List<RepositoryConstraintFinding>();
            var count = 0;
            var limited = false;
            void Finding(string status, string reason, MarkdownReferenceSpan? span = null)
            {
                count++;
                if (findings.Count < Math.Clamp(findingLimit, 1, 1000)) findings.Add(new(sourcePath, status, reason, span?.Line, span?.Column));
            }
            if (applicable)
            {
                var exception = rule.Exceptions.FirstOrDefault(item => Match(item.Path, sourcePath));
                if (exception is not null) Finding("excepted", exception.Reason);
                else foreach (var resolution in resolutions.Where(item => rule.ReferenceKinds.Contains(item.Reference.Kind, StringComparer.Ordinal)))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var reference = resolution.Reference;
                    if (resolution.Status == "external") continue;
                    if (resolution.Status is "unsupported" or "excluded" or "unobserved" || resolution.FragmentStatus == "unverified") limited = true;
                    if (resolution.Status is "unsupported" or "outside_repository" or "excluded" or "unobserved" or "fragment_only")
                        Finding(resolution.Status, resolution.Reason, reference.SourceSpan);
                    else if (resolution.Status == "missing" && rule.RequireLocalResolution)
                        Finding("missing", $"{rule.Reason} Target: {reference.NormalizedPath}", reference.SourceSpan);
                    else if (resolution.Status == "missing" && rule.AllowedTargetGroups.Count > 0)
                    {
                        limited = true;
                        Finding("unverified_target_group", "An absent target has no observed group membership.", reference.SourceSpan);
                    }
                    if (rule.AllowedTargetGroups.Count > 0 && reference.NormalizedPath is not null
                        && files.TryGetValue(reference.NormalizedPath, out var target)
                        && !rule.AllowedTargetGroups.Any(group => InGroup(groups[group], target)))
                        Finding("forbidden", $"{rule.Reason} Target: {target.Path}", reference.SourceSpan);
                    if (resolution.FragmentStatus == "unverified" && resolution.Status != "fragment_only")
                        Finding("unverified_fragment", "Target fragments require renderer-specific anchor evidence.", reference.SourceSpan);
                }
            }
            outcomes.Add(new(rule.Id, "markdown_reference", rule.Severity,
                !complete || limited ? "incomplete" : !applicable ? "not_applicable" : "evaluated",
                !complete || limited ? "partial" : "complete", rule.Reason, ["repository_inventory", "markdown_references"],
                applicable ? 1 : 0, count, count > findings.Count, findings));
        }
        var incomplete = !complete || outcomes.Any(outcome => outcome.Status == "incomplete");
        return new(new(inventory.Generation, incomplete ? "incomplete" : "evaluated", incomplete ? "partial" : "complete",
            "observed", outcomes, document.Diagnostics), resolutions);

        bool IsExcluded(string path) => Excluded(path, excluded);
    }

    private static bool Excluded(string path, HashSet<string> excluded)
    {
        while (true)
        {
            if (excluded.Contains(path)) return true;
            var separator = path.LastIndexOf('/');
            if (separator < 0) return false;
            path = path[..separator];
        }
    }

    private static bool ValidSpan(MarkdownReferenceSpan? span) => span is { Offset: >= 0, Length: >= 0, Line: > 0, Column: > 0 };
}
