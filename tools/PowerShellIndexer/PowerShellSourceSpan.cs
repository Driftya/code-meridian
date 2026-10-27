namespace CodeMeridian.PowerShellIndexer;

public sealed record PowerShellSourceSpan(int Offset, int Length, int Line, int Column, int EndLine, int EndColumn);
