using Microsoft.CodeAnalysis;

namespace CodeMeridian.RoslynIndexer.Pipeline;

internal sealed class CSharpAnalysisReportBuilder(string rootPath, int requestedFiles, int skippedFiles)
{
    private readonly List<CSharpAnalysisDiagnostic> _samples = [];
    private int _readFiles;
    private int _extractedFiles;
    private int _failedFiles;
    private int _parseErrors;

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

    private void Add(FileInfo file, string stage, string code, int? line)
    {
        // Store locations and codes, never source snippets or exception/diagnostic messages.
        if (_samples.Count < 8)
            _samples.Add(new(Path.GetRelativePath(rootPath, file.FullName).Replace('\\', '/'), stage, code, line));
    }

    public CSharpAnalysisReport Build() => new(requestedFiles, _readFiles, _extractedFiles,
        _failedFiles, skippedFiles, _parseErrors, _samples.ToArray());
}
