namespace CodeMeridian.Core.CodeGraph;

/// <summary>
/// Aggregated call-graph context for a node that is about to be edited.
/// Returned by <see cref="ICodeGraphRepository.GetContextForEditingAsync"/>.
/// </summary>
public sealed record EditingContext(
    CodeNode? Node,
    IReadOnlyList<CodeNode> Callers,
    IReadOnlyList<CodeNode> Callees,
    IReadOnlyList<CodeNode> Interfaces);
