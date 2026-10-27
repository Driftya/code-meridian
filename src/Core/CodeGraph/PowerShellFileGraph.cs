namespace CodeMeridian.Core.CodeGraph;

public sealed record PowerShellFileGraph(string Path, string Status, string? SourceFingerprint,
    IReadOnlyList<CodeNode> Nodes, IReadOnlyList<CodeEdge> Edges);
