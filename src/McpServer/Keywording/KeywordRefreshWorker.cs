using CodeMeridian.Application.Services;
using Microsoft.Extensions.Options;

namespace CodeMeridian.McpServer.Keywording;

internal sealed class KeywordRefreshWorker(
    BackgroundKeywordRefreshQueue queue,
    IServiceScopeFactory scopeFactory,
    IOptions<KeywordRefreshQueueOptions> options,
    ILogger<KeywordRefreshWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            KeywordRefreshWorkItem firstItem;
            try
            {
                firstItem = await queue.Reader.ReadAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            var batch = new List<KeywordRefreshWorkItem> { firstItem };
            await CollectBatchAsync(batch, stoppingToken);
            await ProcessBatchAsync(batch, stoppingToken);
        }
    }

    private async Task CollectBatchAsync(
        List<KeywordRefreshWorkItem> batch,
        CancellationToken cancellationToken)
    {
        var delay = options.Value.DebounceDelay <= TimeSpan.Zero
            ? TimeSpan.Zero
            : options.Value.DebounceDelay;

        if (delay > TimeSpan.Zero)
            await Task.Delay(delay, cancellationToken);

        while (queue.Reader.TryRead(out var item))
            batch.Add(item);
    }

    private async Task ProcessBatchAsync(
        IReadOnlyCollection<KeywordRefreshWorkItem> batch,
        CancellationToken cancellationToken)
    {
        var groups = batch
            .Where(item => !string.IsNullOrWhiteSpace(item.SourceNodeId))
            .GroupBy(item => item.ProjectContext?.Trim() ?? string.Empty, StringComparer.Ordinal)
            .ToArray();

        if (groups.Length == 0)
            return;

        await using var scope = scopeFactory.CreateAsyncScope();
        var keywordService = scope.ServiceProvider.GetRequiredService<IKeywordGraphService>();

        foreach (var group in groups)
        {
            var projectContext = string.IsNullOrWhiteSpace(group.Key) ? null : group.Key;
            var sourceNodeIds = group
                .Select(item => item.SourceNodeId.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            try
            {
                var refreshSummary = await keywordService.RefreshKeywordsAsync(sourceNodeIds, projectContext, cancellationToken);
                var classificationSummary = await keywordService.ClassifyKeywordsAsync(projectContext, cancellationToken);
                logger.LogInformation(
                    "Keyword refresh queue processed {Count} source nodes for {ProjectContext}. Refresh: {RefreshSummary} Classification: {ClassificationSummary}",
                    sourceNodeIds.Length,
                    projectContext ?? "all-projects",
                    FirstLine(refreshSummary),
                    FirstLine(classificationSummary));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Keyword refresh queue failed for {Count} source nodes in {ProjectContext}.",
                    sourceNodeIds.Length,
                    projectContext ?? "all-projects");
            }
        }
    }

    private static string FirstLine(string value) =>
        value.Split(Environment.NewLine, StringSplitOptions.None).FirstOrDefault() ?? string.Empty;
}

