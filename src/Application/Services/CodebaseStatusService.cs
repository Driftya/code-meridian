using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;

namespace CodeMeridian.Application.Services;

public sealed class CodebaseStatusService(
    ICodeGraphRepository codeGraph,
    IVectorRepository vectorStore,
    IEmbeddingProvider embeddingProvider,
    ICodebaseQueryService queryService) : ICodebaseStatusService
{
    public async Task<DoctorStatus> GetDoctorStatusAsync(string? projectContext = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var nodeCountTask = codeGraph.CountCodeNodesAsync(projectContext, cancellationToken);
            var callEdgeCountTask = codeGraph.CountCallEdgesAsync(projectContext, cancellationToken);
            var diagnosticCountTask = codeGraph.CountDiagnosticsAsync(projectContext, cancellationToken);
            var documentCountTask = vectorStore.CountAsync(projectContext, cancellationToken);
            var embeddingAvailableTask = SafeIsEmbeddingAvailableAsync(cancellationToken);
            var graphDriftTask = SafeFindGraphDriftAsync(projectContext, cancellationToken);

            await Task.WhenAll(
                nodeCountTask,
                callEdgeCountTask,
                diagnosticCountTask,
                documentCountTask,
                embeddingAvailableTask,
                graphDriftTask);

            return new DoctorStatus(
                projectContext,
                true,
                await nodeCountTask,
                await callEdgeCountTask,
                await documentCountTask,
                await diagnosticCountTask,
                ParseDriftSeverity(await graphDriftTask),
                await graphDriftTask,
                await embeddingAvailableTask,
                embeddingProvider.ProviderName,
                embeddingProvider.Dimensions);
        }
        catch (Exception ex)
        {
            var embeddingAvailable = await SafeIsEmbeddingAvailableAsync(cancellationToken);
            return new DoctorStatus(
                projectContext,
                false,
                0,
                0,
                0,
                0,
                "high",
                ex.Message,
                embeddingAvailable,
                embeddingProvider.ProviderName,
                embeddingProvider.Dimensions,
                ex.Message);
        }
    }

    private async Task<bool> SafeIsEmbeddingAvailableAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await embeddingProvider.IsAvailableAsync(cancellationToken);
        }
        catch
        {
            return false;
        }
    }

    private async Task<string> SafeFindGraphDriftAsync(string? projectContext, CancellationToken cancellationToken)
    {
        try
        {
            return await queryService.FindGraphDriftAsync(projectContext, limit: 10, cancellationToken);
        }
        catch
        {
            return "Graph drift: high";
        }
    }

    private static string ParseDriftSeverity(string driftReport)
    {
        if (driftReport.Contains("graph drift: low", StringComparison.OrdinalIgnoreCase)
            || driftReport.Contains("**Drift:** low", StringComparison.OrdinalIgnoreCase))
            return "low";

        if (driftReport.Contains("graph drift: moderate", StringComparison.OrdinalIgnoreCase)
            || driftReport.Contains("**Drift:** moderate", StringComparison.OrdinalIgnoreCase))
            return "moderate";

        if (driftReport.Contains("graph drift: high", StringComparison.OrdinalIgnoreCase)
            || driftReport.Contains("**Drift:** high", StringComparison.OrdinalIgnoreCase))
            return "high";

        return "unknown";
    }
}
