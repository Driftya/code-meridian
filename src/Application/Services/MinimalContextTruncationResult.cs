namespace CodeMeridian.Application.Services;

public sealed record MinimalContextTruncationResult(
    bool Callers,
    bool Callees,
    bool Interfaces,
    bool Impact,
    bool Downstream,
    bool CoverageGaps,
    bool Tests,
    bool Files,
    bool Snippets);
