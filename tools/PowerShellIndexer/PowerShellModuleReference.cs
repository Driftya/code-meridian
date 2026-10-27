namespace CodeMeridian.PowerShellIndexer;

public sealed record PowerShellModuleReference(string Kind, string OwnerId, string? Target, string ExpressionKind,
    PowerShellSourceSpan Span, bool OptionsSupported)
{
    public string BindingStatus { get; init; } = "indeterminate";
    public string Reason { get; init; } = "not_bound";
    public string? TargetId { get; init; }
    public IReadOnlyDictionary<string, string> Constraints { get; init; } = new Dictionary<string, string>();
}
