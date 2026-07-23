using CodeMeridian.Core.CodeGraph;

namespace CodeMeridian.Application.Services;

public sealed partial class CodebaseQueryService
{
    private IndexedFileRole ResolveFileRole(CodeNode node)
    {
        var pathRole = string.IsNullOrWhiteSpace(node.FilePath)
            ? IndexedFileRole.Unknown
            : fileRoleClassifier.Classify(node.FilePath);
        if (pathRole == IndexedFileRole.Test)
            return IndexedFileRole.Test;

        if (node.FileRole != IndexedFileRole.Unknown)
            return node.FileRole;

        return pathRole;
    }

    private bool HasUnmistakableTestRoleConflict(CodeNode node) =>
        node.FileRole is not IndexedFileRole.Unknown and not IndexedFileRole.Test
        && !string.IsNullOrWhiteSpace(node.FilePath)
        && fileRoleClassifier.Classify(node.FilePath) == IndexedFileRole.Test;

    private bool AllowsProfile(CodeNode node, AnalysisProfile profile) =>
        analysisProfilePolicy.Allows(profile, ResolveFileRole(node));

    private IReadOnlyList<CodeNode> FilterNodesByProfile(IEnumerable<CodeNode> nodes, AnalysisProfile profile) =>
        nodes.Where(node => AllowsProfile(node, profile)).ToArray();

    private IReadOnlyList<(CodeNode Node, T Value)> FilterNodePairsByProfile<T>(
        IEnumerable<(CodeNode Node, T Value)> nodes,
        AnalysisProfile profile) =>
        nodes.Where(item => AllowsProfile(item.Node, profile)).ToArray();
}
