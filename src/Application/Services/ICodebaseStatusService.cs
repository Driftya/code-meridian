using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;

namespace CodeMeridian.Application.Services;

public interface ICodebaseStatusService
{
    Task<DoctorStatus> GetDoctorStatusAsync(string? projectContext = null, CancellationToken cancellationToken = default);
}

public sealed record DoctorStatus(
    string? ProjectContext,
    bool Neo4jReachable,
    long IndexedNodes,
    long CallEdges,
    long DocumentsIndexed,
    long DiagnosticsIndexed,
    string GraphDrift,
    string GraphDriftReport,
    bool EmbeddingsEnabled,
    string EmbeddingProvider,
    int EmbeddingDimensions,
    string? Error = null);
