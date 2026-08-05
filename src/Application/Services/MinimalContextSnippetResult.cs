using System.Text.Json.Serialization;

namespace CodeMeridian.Application.Services;

public sealed record MinimalContextSnippetResult(
    GraphNodeResult Node,
    int EstimatedTokens,
    bool Truncated,
    [property: JsonIgnore] string MarkdownText);
