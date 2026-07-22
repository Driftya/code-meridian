namespace CodeMeridian.Application.Services;

public sealed partial class CodebaseQueryService
{
    private async Task<TResult> WithResolvedAnalysisOptionsAsync<TResult>(
        string? projectContext,
        CancellationToken cancellationToken,
        Func<Task<TResult>> action)
    {
        var resolution = await analysisOptionsResolver.ResolveAsync(projectContext, cancellationToken);
        using var _ = new AnalysisOptionsScope(this, resolution);
        return await action();
    }

    private async Task<string?> ResolveAnalysisProjectContextForNodeAsync(
        string nodeId,
        string? fallbackProjectContext,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(fallbackProjectContext))
            return await ResolveProjectContextAsync(fallbackProjectContext, cancellationToken);

        var context = await codeGraph.GetContextForEditingAsync(nodeId, cancellationToken);
        return await ResolveProjectContextAsync(context?.Node?.ProjectContext, cancellationToken);
    }

    private sealed class AnalysisOptionsScope : IDisposable
    {
        private readonly CodebaseQueryService owner;
        private readonly ResolvedProjectAnalysisOptions? previous;
        private bool disposed;

        public AnalysisOptionsScope(
            CodebaseQueryService owner,
            ResolvedProjectAnalysisOptions current)
        {
            this.owner = owner;
            previous = owner.currentAnalysisResolution.Value;
            owner.currentAnalysisResolution.Value = current;
        }

        public void Dispose()
        {
            if (disposed)
                return;

            owner.currentAnalysisResolution.Value = previous;
            disposed = true;
        }
    }
}
