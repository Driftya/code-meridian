namespace CodeMeridian.Application.Services;

public sealed partial class CodebaseQueryService
{
    private async Task<string?> ResolveProjectContextAsync(
        string? projectContext,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(projectContext))
            return projectContext;

        var candidates = await codeGraph.GetProjectContextsAsync(projectContext, cancellationToken);
        if (candidates.Count == 0)
            return projectContext;

        var normalizedRequested = NormalizeProjectContextForSuggestion(projectContext);
        var canonical = candidates.FirstOrDefault(candidate =>
            string.Equals(
                NormalizeProjectContextForSuggestion(candidate),
                normalizedRequested,
                StringComparison.Ordinal));

        return canonical ?? projectContext;
    }
}
