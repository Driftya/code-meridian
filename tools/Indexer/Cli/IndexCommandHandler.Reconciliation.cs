namespace CodeMeridian.Indexer.Cli.Commands;

internal sealed partial class IndexCommandHandler
{
    private async Task<IndexExecutionContext?> ReconcileExecutionContextAsync(IndexExecutionContext context, bool clear,
        CancellationToken cancellationToken = default)
    {
        if (clear || !_settings.Incremental || _settings.ExternalOnly) return context;
        try
        {
            if (await _profileReporter.IsCacheCurrentAsync(_settings, context.Cache, cancellationToken,
                verifyInventory: !context.IncrementalPlan.HasChanges)) return context;
            Console.WriteLine("The cache has no matching current server profile; running a full selection to refresh publication.");
            return context with { ChangedFiles = null };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Console.Error.WriteLine($"Profile reconciliation failed; the index cache was retained: {ex.Message}");
            return null;
        }
    }
}
