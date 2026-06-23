using CodeMeridian.Core.CodeGraph;

namespace CodeMeridian.Application.Services;

public sealed partial class CodebaseQueryService
{
    private async Task<string> ResolveCanonicalNodeIdAsync(
        string nodeId,
        string? projectContext = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(nodeId) || nodeId.Contains("::", StringComparison.Ordinal))
            return nodeId;

        var searchTerm = ExtractNodeSearchTerm(nodeId);
        if (string.IsNullOrWhiteSpace(searchTerm))
            return nodeId;

        var candidates = await codeGraph.QueryNodesAsync(
            new CodeGraphQuery
            {
                NameFilter = searchTerm,
                ProjectContext = projectContext,
                Limit = 50
            },
            cancellationToken);

        var rankedMatches = candidates
            .Where(node => MatchesNodeIdHint(node, nodeId))
            .OrderBy(node => string.Equals(node.Id, nodeId, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(node => node.Id.EndsWith(nodeId, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(node => node.Type == CodeNodeType.File ? 1 : 0)
            .ThenBy(NodeDisplayRank)
            .ThenBy(node => node.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (rankedMatches.Length == 1)
            return rankedMatches[0].Id;

        var exactSuffixMatches = rankedMatches
            .Where(node => node.Id.EndsWith(nodeId, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();

        return exactSuffixMatches.Length == 1
            ? exactSuffixMatches[0].Id
            : nodeId;
    }

    private static string ExtractNodeSearchTerm(string nodeId)
    {
        var trimmed = nodeId.Trim();
        if (trimmed.Length == 0)
            return trimmed;

        if (trimmed.Contains('/') || trimmed.Contains('\\'))
            return Path.GetFileNameWithoutExtension(trimmed);

        var withoutSignature = trimmed.Split('(')[0];
        var lastDot = withoutSignature.LastIndexOf('.');
        return lastDot >= 0 && lastDot < withoutSignature.Length - 1
            ? withoutSignature[(lastDot + 1)..]
            : withoutSignature;
    }

    private static bool MatchesNodeIdHint(CodeNode node, string hint)
    {
        if (string.Equals(node.Id, hint, StringComparison.OrdinalIgnoreCase))
            return true;

        if (node.Id.EndsWith(hint, StringComparison.OrdinalIgnoreCase))
            return true;

        if (string.IsNullOrWhiteSpace(node.FilePath))
            return false;

        return string.Equals(node.FilePath, hint, StringComparison.OrdinalIgnoreCase)
               || node.FilePath.EndsWith(hint, StringComparison.OrdinalIgnoreCase);
    }
}
