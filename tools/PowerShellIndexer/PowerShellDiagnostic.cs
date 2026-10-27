namespace CodeMeridian.PowerShellIndexer;

public sealed record PowerShellDiagnostic(string Code, string Message, PowerShellSourceSpan? Span, bool Truncated = false);
