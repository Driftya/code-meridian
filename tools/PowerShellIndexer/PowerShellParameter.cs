namespace CodeMeridian.PowerShellIndexer;

public sealed record PowerShellParameter(string Name, IReadOnlyList<string> TypeConstraints, bool HasDefault, PowerShellSourceSpan Span);
