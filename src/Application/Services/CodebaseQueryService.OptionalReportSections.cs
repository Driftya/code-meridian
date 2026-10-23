namespace CodeMeridian.Application.Services;

public sealed partial class CodebaseQueryService
{
    private async Task<OptionalReportSection<T>> ReadOptionalReportSectionAsync<T>(string tool, string? project,
        Func<Task<T>> query, CancellationToken cancellationToken) where T : class
    {
        var before = await EvaluateToolAsync(tool, project, null, cancellationToken);
        if (before is { Status: not "ready" }) return new(null, before.ToMarkdown(tool));
        T value;
        try { value = await query(); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new(null, $"Section execution failed ({ex.GetType().Name}); coverage is unknown.");
        }
        if (before is not null)
        {
            var after = await RevalidateToolAsync(tool, before, null, cancellationToken);
            if (after.Status != "ready") return new(null, after.ToMarkdown(tool));
        }
        return new(value, null);
    }

    private sealed record OptionalReportSection<T>(T? Value, string? UnavailableReason) where T : class;
}
