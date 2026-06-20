using System.Text;
using CodeMeridian.Core.CodeGraph;

namespace CodeMeridian.Application.Services;

public sealed partial class CodebaseQueryService
{
    private const int FrontendCascadeQueryLimit = 10000;

    public async Task<string> FindFrontendCascadeConflictsAsync(
        string? projectContext = null,
        string? filter = null,
        bool excludeTests = true,
        CancellationToken cancellationToken = default)
    {
        var nodes = await codeGraph.QueryNodesAsync(
            new CodeGraphQuery
            {
                ProjectContext = projectContext,
                TypeFilter = CodeNodeType.ExternalConcept,
                Limit = FrontendCascadeQueryLimit
            },
            cancellationToken);

        var declarations = nodes
            .Where(node => AllowsProfile(node, AnalysisProfile.DuplicateDetection))
            .Where(node => !excludeTests || ResolveFileRole(node) != IndexedFileRole.Test)
            .Select(TryMapCascadeDeclaration)
            .OfType<CascadeDeclarationFact>()
            .Where(fact => MatchesCascadeFilter(fact, filter))
            .ToArray();

        if (declarations.Length == 0)
        {
            return "No indexed frontend declarations with cascade metadata were available. " +
                   "Re-index HTML/CSS/SCSS files with the updated frontend indexer, or broaden the current filter.";
        }

        var conflicts = BuildCascadeConflicts(declarations)
            .OrderByDescending(conflict => conflict.PriorityScore)
            .ThenBy(conflict => conflict.PropertyName, StringComparer.Ordinal)
            .ThenBy(conflict => conflict.FilePath, StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .ToArray();

        var suspicious = BuildSpecificityWarnings(declarations)
            .OrderByDescending(warning => warning.PriorityScore)
            .ThenBy(warning => warning.FilePath, StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToArray();

        if (conflicts.Length == 0 && suspicious.Length == 0)
        {
            return "No likely frontend cascade conflicts were found in the indexed declarations. " +
                   "This report only compares declarations within the same stylesheet and shared class/ID target.";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"## Frontend Cascade Conflicts{(projectContext is not null ? $" - {projectContext}" : "")}");
        sb.AppendLine($"**{conflicts.Length}** likely override/conflict relationships and **{suspicious.Length}** specificity warnings from **{declarations.Length}** indexed style declarations.");
        sb.AppendLine();

        if (conflicts.Length > 0)
        {
            sb.AppendLine("| Property | Shared target | Likely winner | Likely shadowed | Why | Confidence |");
            sb.AppendLine("|---|---|---|---|---|---|");

            foreach (var conflict in conflicts)
            {
                var winner = $"`{conflict.Winner.SelectorText}`<br>`{conflict.FilePath}:{conflict.Winner.LineNumber}`";
                var loser = $"`{conflict.Loser.SelectorText}`<br>`{conflict.FilePath}:{conflict.Loser.LineNumber}`";
                var target = $"`{conflict.TargetKind}:{conflict.TargetName}`";
                var why = $"{conflict.Reason}<br>`{conflict.Winner.Specificity.Display}` vs `{conflict.Loser.Specificity.Display}`<br>source order `{conflict.Winner.SourceOrder}` vs `{conflict.Loser.SourceOrder}`";
                sb.AppendLine($"| `{conflict.PropertyName}` | {target} | {winner} | {loser} | {EscapeTableCell(why)} | {EscapeTableCell(conflict.ConfidenceNote)} |");
            }

            sb.AppendLine();
        }

        if (suspicious.Length > 0)
        {
            sb.AppendLine($"### Suspiciously Specific Selectors ({suspicious.Length})");
            foreach (var warning in suspicious)
            {
                sb.AppendLine($"- `{warning.SpecificSelector}` in `{warning.FilePath}:{warning.LineNumber}` is more specific than nearby `{warning.SimpleSelector}` for `{warning.PropertyName}` on `{warning.TargetKind}:{warning.TargetName}` ({warning.SpecificityDelta}); review whether the extra selector complexity is still needed.");
            }
            sb.AppendLine();
        }

        sb.AppendLine("> Cascade findings are inferred from indexed selector specificity, shared class/ID targets, and same-stylesheet source order. They do not model full DOM overlap, cross-file import order, or runtime-only classes.");
        return sb.ToString();
    }

    private static bool MatchesCascadeFilter(CascadeDeclarationFact fact, string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return true;

        return fact.PropertyName.Contains(filter, StringComparison.OrdinalIgnoreCase)
               || fact.SelectorText.Contains(filter, StringComparison.OrdinalIgnoreCase)
               || fact.FilePath.Contains(filter, StringComparison.OrdinalIgnoreCase)
               || fact.RawValue.Contains(filter, StringComparison.OrdinalIgnoreCase)
               || fact.Targets.Any(target => target.Name.Contains(filter, StringComparison.OrdinalIgnoreCase));
    }

    private static CascadeDeclarationFact? TryMapCascadeDeclaration(CodeNode node)
    {
        if (node.Properties is null)
            return null;

        if (node.Type != CodeNodeType.ExternalConcept
            || node.Properties.TryGetValue("externalKind", out var kind) is false
            || !string.Equals(kind, "CssDeclaration", StringComparison.Ordinal)
            || !node.Properties.TryGetValue("propertyName", out var propertyName)
            || !node.Properties.TryGetValue("rawValue", out var rawValue)
            || !node.Properties.TryGetValue("selectorText", out var selectorText)
            || !TryGetIntProperty(node, "sourceOrder", out var sourceOrder)
            || !TryGetIntProperty(node, "specificityA", out var specificityA)
            || !TryGetIntProperty(node, "specificityB", out var specificityB)
            || !TryGetIntProperty(node, "specificityC", out var specificityC)
            || string.IsNullOrWhiteSpace(node.FilePath)
            || node.LineNumber is null)
        {
            return null;
        }

        var targets = ParseCascadeTargets(node.Properties);
        if (targets.Length == 0)
            return null;

        return new CascadeDeclarationFact(
            node,
            propertyName.Trim().ToLowerInvariant(),
            rawValue.Trim(),
            selectorText.Trim(),
            node.FilePath!,
            node.LineNumber.Value,
            sourceOrder,
            new CascadeSpecificity(specificityA, specificityB, specificityC),
            targets);
    }

    private static CascadeConflict[] BuildCascadeConflicts(IReadOnlyCollection<CascadeDeclarationFact> declarations)
    {
        var conflicts = new Dictionary<string, CascadeConflict>(StringComparer.Ordinal);

        foreach (var group in declarations
                     .SelectMany(declaration => declaration.Targets.Select(target => new CascadeGrouping(declaration, target)))
                     .GroupBy(item => $"{item.Declaration.FilePath}|{item.Declaration.PropertyName}|{item.Target.Kind}|{item.Target.Name}", StringComparer.Ordinal))
        {
            var items = group
                .OrderBy(item => item.Declaration.SourceOrder)
                .ThenBy(item => item.Declaration.LineNumber)
                .ToArray();

            for (var leftIndex = 0; leftIndex < items.Length; leftIndex++)
            {
                for (var rightIndex = leftIndex + 1; rightIndex < items.Length; rightIndex++)
                {
                    var left = items[leftIndex].Declaration;
                    var right = items[rightIndex].Declaration;
                    var comparison = CompareCascadePriority(right, left);
                    if (comparison == 0)
                        continue;

                    var winner = comparison > 0 ? right : left;
                    var loser = comparison > 0 ? left : right;
                    var target = items[leftIndex].Target;
                    var key = $"{winner.Node.Id}|{loser.Node.Id}|{target.Kind}|{target.Name}|{winner.PropertyName}";
                    if (conflicts.ContainsKey(key))
                        continue;

                    var reason = CompareSpecificity(winner.Specificity, loser.Specificity) > 0
                        ? $"higher specificity `{winner.Specificity.Display}` over `{loser.Specificity.Display}`"
                        : $"same specificity `{winner.Specificity.Display}` and later source order";
                    var confidence = "inferred from shared class/ID target plus same-stylesheet specificity/order metadata";
                    conflicts[key] = new CascadeConflict(
                        winner.PropertyName,
                        target.Kind,
                        target.Name,
                        winner.FilePath,
                        winner,
                        loser,
                        reason,
                        confidence,
                        BuildConflictPriorityScore(winner, loser));
                }
            }
        }

        return conflicts.Values.ToArray();
    }

    private static SpecificityWarning[] BuildSpecificityWarnings(IReadOnlyCollection<CascadeDeclarationFact> declarations)
    {
        var warnings = new Dictionary<string, SpecificityWarning>(StringComparer.Ordinal);

        foreach (var group in declarations
                     .SelectMany(declaration => declaration.Targets.Select(target => new CascadeGrouping(declaration, target)))
                     .GroupBy(item => $"{item.Declaration.FilePath}|{item.Declaration.PropertyName}|{item.Target.Kind}|{item.Target.Name}", StringComparer.Ordinal))
        {
            var items = group.ToArray();
            var simpleSelectors = items
                .Where(item => IsSimpleTargetSelector(item.Declaration.SelectorText, item.Target))
                .ToArray();

            foreach (var simple in simpleSelectors)
            {
                foreach (var candidate in items)
                {
                    if (candidate.Declaration.Node.Id == simple.Declaration.Node.Id)
                        continue;

                    var specificityDelta = CompareSpecificity(candidate.Declaration.Specificity, simple.Declaration.Specificity);
                    if (specificityDelta <= 0)
                        continue;

                    if (Math.Abs(candidate.Declaration.SourceOrder - simple.Declaration.SourceOrder) > 6)
                        continue;

                    var key = $"{candidate.Declaration.Node.Id}|{simple.Declaration.Node.Id}|{candidate.Target.Kind}|{candidate.Target.Name}|{candidate.Declaration.PropertyName}";
                    warnings[key] = new SpecificityWarning(
                        candidate.Declaration.FilePath,
                        candidate.Declaration.LineNumber,
                        candidate.Target.Kind,
                        candidate.Target.Name,
                        candidate.Declaration.PropertyName,
                        candidate.Declaration.SelectorText,
                        simple.Declaration.SelectorText,
                        $"{candidate.Declaration.Specificity.Display} vs {simple.Declaration.Specificity.Display}",
                        (specificityDelta * 100) + (6 - Math.Abs(candidate.Declaration.SourceOrder - simple.Declaration.SourceOrder)));
                }
            }
        }

        return warnings.Values.ToArray();
    }

    private static bool IsSimpleTargetSelector(string selectorText, CascadeTarget target) =>
        target.Kind switch
        {
            "CssClass" => string.Equals(selectorText, $".{target.Name}", StringComparison.Ordinal),
            "CssId" => string.Equals(selectorText, $"#{target.Name}", StringComparison.Ordinal),
            _ => false
        };

    private static int CompareCascadePriority(CascadeDeclarationFact left, CascadeDeclarationFact right)
    {
        var specificity = CompareSpecificity(left.Specificity, right.Specificity);
        if (specificity != 0)
            return specificity;

        if (left.SourceOrder != right.SourceOrder)
            return left.SourceOrder - right.SourceOrder;

        return left.LineNumber - right.LineNumber;
    }

    private static int CompareSpecificity(CascadeSpecificity left, CascadeSpecificity right)
    {
        if (left.Ids != right.Ids)
            return left.Ids - right.Ids;

        if (left.Classes != right.Classes)
            return left.Classes - right.Classes;

        return left.Elements - right.Elements;
    }

    private static int BuildConflictPriorityScore(CascadeDeclarationFact winner, CascadeDeclarationFact loser)
    {
        var specificityDelta = CompareSpecificity(winner.Specificity, loser.Specificity);
        if (specificityDelta != 0)
            return 1000 + (specificityDelta * 100) + winner.SourceOrder;

        return 500 + winner.SourceOrder;
    }

    private static bool TryGetIntProperty(CodeNode node, string key, out int value)
    {
        value = 0;
        return node.Properties.TryGetValue(key, out var raw)
               && int.TryParse(raw, out value);
    }

    private static CascadeTarget[] ParseCascadeTargets(IReadOnlyDictionary<string, string> properties)
    {
        var targets = new List<CascadeTarget>();
        if (properties.TryGetValue("targetClassConceptsCsv", out var classTargets))
        {
            targets.AddRange(classTargets
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(name => new CascadeTarget("CssClass", name)));
        }

        if (properties.TryGetValue("targetIdConceptsCsv", out var idTargets))
        {
            targets.AddRange(idTargets
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(name => new CascadeTarget("CssId", name)));
        }

        return targets
            .DistinctBy(target => $"{target.Kind}|{target.Name}", StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private sealed record CascadeDeclarationFact(
        CodeNode Node,
        string PropertyName,
        string RawValue,
        string SelectorText,
        string FilePath,
        int LineNumber,
        int SourceOrder,
        CascadeSpecificity Specificity,
        IReadOnlyCollection<CascadeTarget> Targets);

    private sealed record CascadeSpecificity(int Ids, int Classes, int Elements)
    {
        public string Display => $"{Ids},{Classes},{Elements}";
    }

    private sealed record CascadeTarget(string Kind, string Name);

    private sealed record CascadeGrouping(CascadeDeclarationFact Declaration, CascadeTarget Target);

    private sealed record CascadeConflict(
        string PropertyName,
        string TargetKind,
        string TargetName,
        string FilePath,
        CascadeDeclarationFact Winner,
        CascadeDeclarationFact Loser,
        string Reason,
        string ConfidenceNote,
        int PriorityScore);

    private sealed record SpecificityWarning(
        string FilePath,
        int LineNumber,
        string TargetKind,
        string TargetName,
        string PropertyName,
        string SpecificSelector,
        string SimpleSelector,
        string SpecificityDelta,
        int PriorityScore);
}
