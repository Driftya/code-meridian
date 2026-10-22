namespace CodeMeridian.RoslynIndexer.Pipeline;

public sealed record CSharpAnalysisDiagnostic(string FilePath, string Stage, string Code, int? LineNumber);
