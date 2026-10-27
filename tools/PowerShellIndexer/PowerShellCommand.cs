namespace CodeMeridian.PowerShellIndexer;

public sealed record PowerShellCommand(string Id, string OwnerId, string? Name, string Invocation,
    string ExpressionKind, string? PathValue, PowerShellSourceSpan Span, string Snippet, bool Deferred)
{
    public string BindingStatus { get; init; } = "indeterminate";
    public string Reason { get; init; } = "not_bound";
    public string? TargetId { get; init; }
}
