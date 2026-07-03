using CodeMeridian.Core.CodeGraph;

namespace CodeMeridian.Application.Services;

internal enum ActionabilityBucket
{
    ProductionCandidate,
    BroaderHeuristicMatch,
    SuppressedNoise
}

internal sealed record NodeActionabilityAssessment(
    ActionabilityBucket Bucket,
    string Confidence,
    int RankPenalty,
    string Reason);

internal sealed record ScoredNodeDisplayItem<T>(
    CodeNode Node,
    T Value,
    NodeActionabilityAssessment Assessment);

internal sealed record RankedNodeSections<T>(
    IReadOnlyList<ScoredNodeDisplayItem<T>> ProductionCandidates,
    IReadOnlyList<ScoredNodeDisplayItem<T>> BroaderHeuristicMatches,
    IReadOnlyList<ScoredNodeDisplayItem<T>> SuppressedNoise);

public sealed partial class CodebaseQueryService
{
    private IEnumerable<CodeNode> RankNodesForDisplay(IEnumerable<CodeNode> nodes) =>
        nodes.Select(node => new ScoredNodeDisplayItem<int>(node, 0, AssessActionability(node)))
            .OrderBy(item => item.Assessment.Bucket)
            .ThenBy(item => item.Assessment.RankPenalty)
            .ThenByDescending(item => item.Node.ChangeCount ?? 0)
            .ThenByDescending(item => item.Node.LineCount ?? 0)
            .ThenBy(item => item.Node.Type.ToString(), StringComparer.Ordinal)
            .ThenBy(item => item.Node.Name, StringComparer.OrdinalIgnoreCase)
            .Select(item => item.Node);

    private IEnumerable<(CodeNode Node, T Value)> RankScoredNodesForDisplay<T>(IEnumerable<(CodeNode Node, T Value)> nodes) =>
        nodes.Select(item => new ScoredNodeDisplayItem<T>(item.Node, item.Value, AssessActionability(item.Node)))
            .OrderBy(item => item.Assessment.Bucket)
            .ThenBy(item => item.Assessment.RankPenalty)
            .ThenByDescending(item => item.Node.ChangeCount ?? 0)
            .ThenByDescending(item => item.Node.LineCount ?? 0)
            .ThenBy(item => item.Node.Type.ToString(), StringComparer.Ordinal)
            .ThenBy(item => item.Node.Name, StringComparer.OrdinalIgnoreCase)
            .Select(item => (item.Node, item.Value));

    private RankedNodeSections<T> PartitionScoredNodesForDisplay<T>(IEnumerable<(CodeNode Node, T Value)> nodes)
    {
        var ranked = nodes.Select(item => new ScoredNodeDisplayItem<T>(item.Node, item.Value, AssessActionability(item.Node)))
            .OrderBy(item => item.Assessment.Bucket)
            .ThenBy(item => item.Assessment.RankPenalty)
            .ThenByDescending(item => item.Node.ChangeCount ?? 0)
            .ThenByDescending(item => item.Node.LineCount ?? 0)
            .ThenBy(item => item.Node.Type.ToString(), StringComparer.Ordinal)
            .ThenBy(item => item.Node.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new RankedNodeSections<T>(
            ranked.Where(item => item.Assessment.Bucket == ActionabilityBucket.ProductionCandidate).ToArray(),
            ranked.Where(item => item.Assessment.Bucket == ActionabilityBucket.BroaderHeuristicMatch).ToArray(),
            ranked.Where(item => item.Assessment.Bucket == ActionabilityBucket.SuppressedNoise).ToArray());
    }

    private int NodeDisplayRank(CodeNode node)
    {
        return AssessActionability(node).RankPenalty;
    }

    private NodeActionabilityAssessment AssessActionability(CodeNode node)
    {
        var role = ResolveFileRole(node);

        if (ShouldSuppressFromPrimaryResults(node, role))
        {
            return new NodeActionabilityAssessment(
                ActionabilityBucket.SuppressedNoise,
                node.FilePath is null ? "Low" : "Medium",
                200 + SuppressedNoisePenalty(node),
                DescribeSuppressedNoise(node, role));
        }

        if (IsProductionCandidate(node, role))
        {
            var hasStrongMetadata = node.FilePath is not null
                                    && (!node.LineCount.HasValue || node.LineCount.Value >= analysisOptions.Ranking.MinimumActionableLineCount);
            var confidence = hasStrongMetadata && !IsInfrastructureBoilerplate(node) ? "High" : "Medium";
            var penalty = 0;
            if (node.FilePath is null)
                penalty += 10;
            if (node.LineCount.HasValue && node.LineCount.Value < analysisOptions.Ranking.MinimumActionableLineCount)
                penalty += 10;
            if (IsInfrastructureBoilerplate(node))
                penalty += 15;

            return new NodeActionabilityAssessment(
                ActionabilityBucket.ProductionCandidate,
                confidence,
                penalty,
                IsInfrastructureBoilerplate(node)
                    ? "production anchor with infrastructure-style naming"
                    : "file-backed production code");
        }

        return new NodeActionabilityAssessment(
            ActionabilityBucket.BroaderHeuristicMatch,
            node.FilePath is null ? "Low" : "Medium",
            100 + HeuristicPenalty(node),
            DescribeBroaderHeuristic(node, role));
    }

    private bool IsConfiguredTestNode(CodeNode node)
    {
        var role = ResolveFileRole(node);
        if (role == IndexedFileRole.Test)
            return true;
        if (role != IndexedFileRole.Unknown)
            return false;

        var haystack = $"{node.FilePath} {node.Namespace} {node.Name}";
        return analysisOptions.Ranking.TestPathContains
            .Any(pattern => haystack.Contains(pattern, StringComparison.OrdinalIgnoreCase));
    }

    private bool IsInfrastructureBoilerplate(CodeNode node)
    {
        if (analysisOptions.Ranking.InfrastructureNames.Contains(node.Name, StringComparer.OrdinalIgnoreCase))
            return true;

        return analysisOptions.Ranking.InfrastructureNameSuffixes
            .Any(suffix => node.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
    }

    private bool ShouldShowBroaderHeuristicMatchesInline() =>
        !analysisOptions.Ranking.ProductionOnlyByDefault || analysisOptions.Ranking.IncludeBroaderHeuristicMatches;

    private bool ShouldShowSuppressedNoiseInline() =>
        analysisOptions.Ranking.IncludeSuppressedNoise;

    private bool ShouldSuppressFromPrimaryResults(CodeNode node, IndexedFileRole role)
    {
        if (analysisOptions.Ranking.PreferProductionOverTests && IsConfiguredTestNode(node))
            return true;

        if (analysisOptions.Ranking.SuppressedFileRoles.Contains(role.ToString(), StringComparer.OrdinalIgnoreCase))
            return true;

        return analysisOptions.Ranking.SuppressedNodeTypes.Contains(node.Type.ToString(), StringComparer.OrdinalIgnoreCase);
    }

    private bool IsProductionCandidate(CodeNode node, IndexedFileRole role)
    {
        if (role is not (IndexedFileRole.Source or IndexedFileRole.Unknown))
            return false;

        return node.Type is CodeNodeType.Class
            or CodeNodeType.Struct
            or CodeNodeType.Interface
            or CodeNodeType.Method
            or CodeNodeType.Delegate
            or CodeNodeType.Enum
            or CodeNodeType.Module;
    }

    private int SuppressedNoisePenalty(CodeNode node)
    {
        var penalty = 0;
        if (IsConfiguredTestNode(node))
            penalty += 10;
        if (node.FilePath is null)
            penalty += 5;

        return penalty;
    }

    private int HeuristicPenalty(CodeNode node)
    {
        var penalty = 0;
        if (analysisOptions.Ranking.BroaderHeuristicNodeTypes.Contains(node.Type.ToString(), StringComparer.OrdinalIgnoreCase))
            penalty += 5;
        if (node.FilePath is null)
            penalty += 5;
        if (node.LineCount.HasValue && node.LineCount.Value < analysisOptions.Ranking.MinimumActionableLineCount)
            penalty += 5;

        return penalty;
    }

    private static string DescribeSuppressedNoise(CodeNode node, IndexedFileRole role)
    {
        if (role == IndexedFileRole.Test)
            return "test-only surface";
        if (role == IndexedFileRole.Configuration)
            return "configuration artifact";
        if (role is IndexedFileRole.Migration or IndexedFileRole.Snapshot or IndexedFileRole.Generated or IndexedFileRole.BuildArtifact)
            return "non-production generated or build artifact";

        return node.Type switch
        {
            CodeNodeType.ConfigurationKey or CodeNodeType.ConfigurationEntry => "configuration metadata node",
            CodeNodeType.Diagnostic => "diagnostic artifact",
            CodeNodeType.Property or CodeNodeType.Field or CodeNodeType.Event or CodeNodeType.Indexer or CodeNodeType.Operator => "single-member artifact",
            _ => "suppressed low-actionability node"
        };
    }

    private string DescribeBroaderHeuristic(CodeNode node, IndexedFileRole role)
    {
        if (analysisOptions.Ranking.BroaderHeuristicNodeTypes.Contains(node.Type.ToString(), StringComparer.OrdinalIgnoreCase))
        {
            return node.Type switch
            {
                CodeNodeType.Namespace => "namespace-level aggregate",
                CodeNodeType.File => "file-level aggregate",
                CodeNodeType.ApiEndpoint => "route or endpoint anchor",
                CodeNodeType.ConfigurationFile => "configuration file context",
                CodeNodeType.DatabaseTable => "schema artifact",
                CodeNodeType.ExternalConcept or CodeNodeType.ExternalService or CodeNodeType.MessageTopic => "external or synthetic concept",
                _ => "broader heuristic match"
            };
        }

        if (role == IndexedFileRole.Unknown)
            return "unknown file role with usable code shape";

        return "indirectly actionable code surface";
    }
}
