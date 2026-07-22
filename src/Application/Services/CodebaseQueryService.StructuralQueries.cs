using System.Text;
using CodeMeridian.Core.CodeGraph;

namespace CodeMeridian.Application.Services;

public partial class CodebaseQueryService
{
    private async Task<string?> TryQueryStructuralIntentAsync(
        string query,
        string? projectContext,
        CancellationToken cancellationToken)
    {
        if (!TryParseStructuralIntent(query, out var intent))
            return null;

        if (intent.Kind == StructuralIntentKind.Members)
            return await QueryNamespaceMembersAsync(intent.Target, projectContext, cancellationToken);

        var candidates = await codeGraph.QueryNodesAsync(
            new CodeGraphQuery
            {
                NameFilter = intent.Target,
                ProjectContext = projectContext,
                Limit = 50
            },
            cancellationToken);
        var ranked = candidates
            .Where(node => node.Type is not (CodeNodeType.File or CodeNodeType.Namespace))
            .OrderBy(node => StructuralTargetRank(node, intent.Target))
            .ThenBy(node => node.FilePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(node => node.Id, StringComparer.Ordinal)
            .ToArray();
        if (ranked.Length == 0)
            return $"No exact structural target found for \u0060{intent.Target}\u0060. Use \u0060resolve_exact_symbol\u0060 or provide a canonical node ID.";

        var bestRank = StructuralTargetRank(ranked[0], intent.Target);
        var best = ranked.Where(node => StructuralTargetRank(node, intent.Target) == bestRank).Take(11).ToArray();
        if (best.Length != 1)
            return FormatAmbiguousStructuralTargets(intent.Target, best);

        var target = best[0];
        var context = await codeGraph.GetContextForEditingAsync(target.Id, cancellationToken);
        if (context?.Node is null)
            return $"Target \u0060{target.Id}\u0060 was resolved, but its relationship context is unavailable. Re-index before trusting structural results.";

        var facts = intent.Kind switch
        {
            StructuralIntentKind.Callers => context.Callers,
            StructuralIntentKind.Callees => context.Callees.Concat(context.Interfaces).DistinctBy(node => node.Id).ToArray(),
            StructuralIntentKind.Implementations => context.Callers
                .Where(node => node.Type is CodeNodeType.Class or CodeNodeType.Struct or CodeNodeType.Method)
                .ToArray(),
            _ => []
        };
        var relationshipTrust = await GetRelationshipTrustAsync(target.ProjectContext ?? projectContext, cancellationToken);
        var title = intent.Kind switch
        {
            StructuralIntentKind.Callers => "Callers",
            StructuralIntentKind.Callees => "Dependencies and callees",
            StructuralIntentKind.Implementations => "Implementations",
            _ => "Structural results"
        };
        var builder = new StringBuilder();
        builder.AppendLine($"## {title} — \u0060{target.Name}\u0060");
        builder.AppendLine($"**Canonical target:** \u0060{target.Id}\u0060");
        AppendRelationshipTrustWarning(builder, relationshipTrust);
        if (facts.Count == 0)
        {
            builder.AppendLine("- none observed");
            return builder.ToString();
        }

        foreach (var node in facts.OrderBy(node => node.FilePath, StringComparer.OrdinalIgnoreCase).ThenBy(node => node.Name, StringComparer.OrdinalIgnoreCase))
            builder.AppendLine($"- **{node.Type}** \u0060{node.Name}\u0060 — \u0060{node.FilePath ?? "no file"}\u0060 — \u0060{node.Id}\u0060");
        return builder.ToString();
    }

    private async Task<string> QueryNamespaceMembersAsync(
        string target,
        string? projectContext,
        CancellationToken cancellationToken)
    {
        var nodes = await codeGraph.QueryNodesAsync(
            new CodeGraphQuery { ProjectContext = projectContext, Limit = 500 },
            cancellationToken);
        var matches = nodes
            .Where(node => string.Equals(node.Namespace, target, StringComparison.OrdinalIgnoreCase)
                           || node.Namespace?.StartsWith(target + ".", StringComparison.OrdinalIgnoreCase) == true)
            .Where(node => node.Type is not (CodeNodeType.Namespace or CodeNodeType.File))
            .OrderBy(node => node.Namespace, StringComparer.OrdinalIgnoreCase)
            .ThenBy(node => node.Type)
            .ThenBy(node => node.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (matches.Length == 0)
            return $"No members or types found in namespace/module \u0060{target}\u0060. Check the exact namespace or re-index.";

        var builder = new StringBuilder();
        builder.AppendLine($"## Members in \u0060{target}\u0060");
        foreach (var node in matches)
            builder.AppendLine($"- **{node.Type}** \u0060{node.Name}\u0060 — \u0060{node.FilePath ?? "no file"}\u0060 — \u0060{node.Id}\u0060");
        return builder.ToString();
    }

    private static string FormatAmbiguousStructuralTargets(string target, IReadOnlyCollection<CodeNode> candidates)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Structural target \u0060{target}\u0060 is ambiguous. Choose one canonical ID and retry:");
        foreach (var node in candidates)
            builder.AppendLine($"- \u0060{node.Id}\u0060 — {node.Type} \u0060{node.Name}\u0060 — \u0060{node.FilePath ?? "no file"}\u0060");
        return builder.ToString();
    }

    private static int StructuralTargetRank(CodeNode node, string target)
    {
        var identifier = ExtractRelevantIdentifier(node.Name);
        if (string.Equals(node.Id, target, StringComparison.OrdinalIgnoreCase))
            return 0;
        if (string.Equals(node.Name, target, StringComparison.OrdinalIgnoreCase)
            || string.Equals(identifier, target, StringComparison.OrdinalIgnoreCase))
            return 1;
        if (node.Id.EndsWith(target, StringComparison.OrdinalIgnoreCase))
            return 2;
        return 3;
    }

    private static bool TryParseStructuralIntent(string query, out StructuralIntent intent)
    {
        var normalized = query.Trim().TrimEnd('?', '.', ':');
        foreach (var (prefix, kind) in StructuralPrefixes)
        {
            if (!normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var target = normalized[prefix.Length..].Trim().Trim('\u0060', '"', '\'');
            if (target.Length > 0)
            {
                intent = new StructuralIntent(kind, target);
                return true;
            }
        }

        intent = default;
        return false;
    }

    private static readonly (string Prefix, StructuralIntentKind Kind)[] StructuralPrefixes =
    [
        ("who calls ", StructuralIntentKind.Callers),
        ("callers of ", StructuralIntentKind.Callers),
        ("callees of ", StructuralIntentKind.Callees),
        ("dependencies of ", StructuralIntentKind.Callees),
        ("implementations of ", StructuralIntentKind.Implementations),
        ("classes implementing ", StructuralIntentKind.Implementations),
        ("members in ", StructuralIntentKind.Members),
        ("types in namespace ", StructuralIntentKind.Members),
        ("types in module ", StructuralIntentKind.Members)
    ];

    private enum StructuralIntentKind { Callers, Callees, Implementations, Members }

    private readonly record struct StructuralIntent(StructuralIntentKind Kind, string Target);
}
