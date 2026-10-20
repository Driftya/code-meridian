namespace CodeMeridian.Application.Services;

public sealed partial class CodebaseQueryService
{
    public Task<string> FindImplementationSurfaceAsync(
        string goal,
        string? conceptsCsv = null,
        string? projectContext = null,
        int limit = 12,
        CancellationToken cancellationToken = default)
        => ExecuteCapabilityQueryAsync("find_implementation_surface", projectContext, null,
            () => FindImplementationSurfaceCoreAsync(goal, conceptsCsv, projectContext, limit, cancellationToken), cancellationToken);

    public Task<string> PlanEditRouteAsync(
        string goal,
        string? conceptsCsv = null,
        string? projectContext = null,
        int limit = 8,
        CancellationToken cancellationToken = default)
        => ExecuteCapabilityQueryAsync("plan_edit_route", projectContext, null,
            () => PlanEditRouteCoreAsync(goal, conceptsCsv, projectContext, limit, cancellationToken), cancellationToken);

    public Task<string> ResolveExactSymbolAsync(
        string symbol,
        string? filePath = null,
        int? line = null,
        string? projectContext = null,
        int limit = 10,
        CancellationToken cancellationToken = default)
        => ExecuteCapabilityQueryAsync("resolve_exact_symbol", projectContext, filePath,
            () => ResolveExactSymbolCoreAsync(symbol, filePath, line, projectContext, limit, cancellationToken), cancellationToken);
}
