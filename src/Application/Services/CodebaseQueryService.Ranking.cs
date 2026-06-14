using CodeMeridian.Core.CodeGraph;

namespace CodeMeridian.Application.Services;

public sealed partial class CodebaseQueryService
{
    private IEnumerable<CodeNode> RankNodesForDisplay(IEnumerable<CodeNode> nodes) =>
        nodes.OrderBy(NodeDisplayRank)
            .ThenByDescending(node => node.ChangeCount ?? 0)
            .ThenByDescending(node => node.LineCount ?? 0)
            .ThenBy(node => node.Type.ToString(), StringComparer.Ordinal)
            .ThenBy(node => node.Name, StringComparer.OrdinalIgnoreCase);

    private IEnumerable<(CodeNode Node, T Value)> RankScoredNodesForDisplay<T>(IEnumerable<(CodeNode Node, T Value)> nodes) =>
        nodes.OrderBy(item => NodeDisplayRank(item.Node));

    private int NodeDisplayRank(CodeNode node)
    {
        if (!analysisOptions.Ranking.PreferProductionOverTests)
            return 0;

        var rank = 0;
        if (IsConfiguredTestNode(node))
            rank += 100;
        if (node.Type == CodeNodeType.Namespace)
            rank += 50;
        if (IsInfrastructureBoilerplate(node))
            rank += 20;

        return rank;
    }

    private bool IsConfiguredTestNode(CodeNode node)
    {
        if (ResolveFileRole(node) == IndexedFileRole.Test)
            return true;

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
}
