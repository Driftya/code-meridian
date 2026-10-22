using Microsoft.CodeAnalysis;

namespace CodeMeridian.RoslynIndexer.Pipeline;

internal sealed class CSharpAnalysisReportBuilder(string rootPath, int requestedFiles, int skippedFiles)
{
    private readonly List<CSharpAnalysisDiagnostic> _samples = [];
    private int _readFiles;
    private int _extractedFiles;
    private int _failedFiles;
    private int _parseErrors;
    private int _semanticErrors;

    public void Read() => _readFiles++;
    public void Extracted() => _extractedFiles++;

    public void Failed(FileInfo file, string stage, Exception exception)
    {
        _failedFiles++;
        Add(file, stage, exception.GetType().Name, null);
    }

    public void Parsed(FileInfo file, IEnumerable<Diagnostic> diagnostics)
    {
        foreach (var diagnostic in diagnostics.Where(item => item.Severity == DiagnosticSeverity.Error))
        {
            _parseErrors++;
            Add(file, "parse", diagnostic.Id, diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1);
        }
    }

    public void Semantic(IEnumerable<Diagnostic> diagnostics)
    {
        foreach (var diagnostic in diagnostics.Where(item => item.Severity == DiagnosticSeverity.Error)
            .OrderBy(item => item.Location.SourceTree?.FilePath, StringComparer.Ordinal)
            .ThenBy(item => item.Location.SourceSpan.Start).ThenBy(item => item.Id, StringComparer.Ordinal))
        {
            _semanticErrors++;
            var path = diagnostic.Location.SourceTree?.FilePath;
            if (string.IsNullOrEmpty(path))
            {
                if (_samples.Count < 8) _samples.Add(new(".", "semantic", diagnostic.Id, null));
            }
            else
            {
                Add(new FileInfo(path), "semantic", diagnostic.Id, diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1);
            }
        }
    }

    private void Add(FileInfo file, string stage, string code, int? line)
    {
        // Store locations and codes, never source snippets or exception/diagnostic messages.
        if (_samples.Count < 8)
            _samples.Add(new(Path.GetRelativePath(rootPath, file.FullName).Replace('\\', '/'), stage, code, line));
    }

    public CSharpAnalysisReport Build(RelationshipResolutionStats calls, RelationshipResolutionStats types) =>
        new(requestedFiles, _readFiles, _extractedFiles, _failedFiles, skippedFiles, _parseErrors, _samples.ToArray())
        {
            SemanticErrors = _semanticErrors,
            CallRelationships = CSharpRelationshipSummary.From(calls),
            TypeRelationships = CSharpRelationshipSummary.From(types)
        };
}
