namespace CodeMeridian.Application.Services;

public sealed partial class CodebaseQueryService
{
    private async Task<TResult> WithResolvedAnalysisOptionsAsync<TResult>(
        string? projectContext,
        CancellationToken cancellationToken,
        Func<Task<TResult>> action)
    {
        using var _ = await BeginAnalysisOptionsScopeAsync(projectContext, cancellationToken);
        return await action();
    }

    private async Task<AnalysisOptionsScope> BeginAnalysisOptionsScopeAsync(
        string? projectContext,
        CancellationToken cancellationToken)
    {
        var resolution = await analysisOptionsResolver.ResolveAsync(projectContext, cancellationToken);
        return new AnalysisOptionsScope(this, resolution);
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
            previous = owner.currentAnalysisResolution;
            owner.currentAnalysisResolution = current;
        }

        public void Dispose()
        {
            if (disposed)
                return;

            owner.currentAnalysisResolution = previous;
            disposed = true;
        }
    }
}
