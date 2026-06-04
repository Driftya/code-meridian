namespace CodeMeridian.Core.CodeGraph;

/// <summary>
/// A semantic duplicate-code candidate pair with lightweight refactor-risk signals.
/// </summary>
public sealed record DuplicateCandidate(
    CodeNode Source,
    CodeNode Candidate,
    double Score,
    int SourceFanIn,
    int CandidateFanIn,
    bool SourceHasTestCoverage,
    bool CandidateHasTestCoverage);
