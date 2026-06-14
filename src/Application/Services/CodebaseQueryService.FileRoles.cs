using CodeMeridian.Core.CodeGraph;

namespace CodeMeridian.Application.Services;

public sealed partial class CodebaseQueryService
{
    private IndexedFileRole ResolveFileRole(CodeNode node)
    {
        if (node.FileRole != IndexedFileRole.Unknown)
            return node.FileRole;

        return string.IsNullOrWhiteSpace(node.FilePath)
            ? IndexedFileRole.Unknown
            : fileRoleClassifier.Classify(node.FilePath);
    }

    private bool AllowsProfile(CodeNode node, AnalysisProfile profile) =>
        analysisProfilePolicy.Allows(profile, ResolveFileRole(node));

    private IReadOnlyList<CodeNode> FilterNodesByProfile(IEnumerable<CodeNode> nodes, AnalysisProfile profile) =>
        nodes.Where(node => AllowsProfile(node, profile)).ToArray();

    private IReadOnlyList<(CodeNode Node, T Value)> FilterNodePairsByProfile<T>(
        IEnumerable<(CodeNode Node, T Value)> nodes,
        AnalysisProfile profile) =>
        nodes.Where(item => AllowsProfile(item.Node, profile)).ToArray();
}
