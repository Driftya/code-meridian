using CodeMeridian.Core.CodeGraph;

namespace CodeMeridian.Application.Services;

public sealed record GraphNodeResult(
    string Id,
    string Name,
    string Type,
    string? Namespace,
    string? FilePath,
    int? LineNumber,
    int? LineCount,
    string? ProjectContext,
    string? Summary)
{
    internal static GraphNodeResult FromNode(CodeNode node) =>
        new(
            node.Id,
            node.Name,
            node.Type.ToString(),
            node.Namespace,
            node.FilePath,
            node.LineNumber,
            node.LineCount,
            node.ProjectContext,
            Bound(node.Summary, 1_000));

    internal string FormatLocation()
    {
        if (FilePath is null)
            return string.Empty;

        return $" — `{FilePath}`{(LineNumber.HasValue ? $":{LineNumber}" : string.Empty)}";
    }

    private static string? Bound(string? value, int maximumCharacters) =>
        value is null || value.Length <= maximumCharacters
            ? value
            : value[..maximumCharacters];
}
