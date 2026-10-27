namespace CodeMeridian.PowerShellIndexer;

public sealed record PowerShellBody(string Id, string? ParentId, string Kind, string Name, PowerShellSourceSpan Span,
    PowerShellSourceSpan BodySpan, string Snippet, IReadOnlyList<PowerShellParameter> Parameters, bool Conditional);
