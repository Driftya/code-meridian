using Microsoft.Extensions.DependencyInjection;

namespace CodeMeridian.Application.Services;

public sealed class KeywordGraphJobService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider) : IKeywordGraphJobService
{
    private static readonly TimeSpan DefaultLeaseTtl = TimeSpan.FromMinutes(30);
    private readonly object _gate = new();
    private readonly Dictionary<Guid, JobRecord> _jobsById = new();
    private readonly Dictionary<string, Guid> _activeJobIdsByKey = new(StringComparer.Ordinal);

    public Task<KeywordGraphJobSubmissionResult> StartRebuildAsync(
        string? projectContext = null,
        TimeSpan? leaseTtl = null,
        CancellationToken cancellationToken = default) =>
        StartAsync(
            "rebuild",
            projectContext,
            leaseTtl ?? DefaultLeaseTtl,
            cancellationToken,
            static (service, project, ct) => service.RebuildKeywordGraphAsync(project, ct));

    public Task<KeywordGraphJobSubmissionResult> StartClassifyAsync(
        string? projectContext = null,
        TimeSpan? leaseTtl = null,
        CancellationToken cancellationToken = default) =>
        StartAsync(
            "classify",
            projectContext,
            leaseTtl ?? DefaultLeaseTtl,
            cancellationToken,
            static (service, project, ct) => service.ClassifyKeywordsAsync(project, ct));

    public Task<KeywordGraphJobStatus?> GetStatusAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!_jobsById.TryGetValue(jobId, out var job))
                return Task.FromResult<KeywordGraphJobStatus?>(null);

            return Task.FromResult<KeywordGraphJobStatus?>(job.ToStatus(timeProvider.GetUtcNow()));
        }
    }

    private Task<KeywordGraphJobSubmissionResult> StartAsync(
        string operation,
        string? projectContext,
        TimeSpan leaseTtl,
        CancellationToken cancellationToken,
        Func<IKeywordGraphService, string?, CancellationToken, Task<string>> runJob)
    {
        var now = timeProvider.GetUtcNow();
        var key = BuildKey(projectContext);

        lock (_gate)
        {
            if (_activeJobIdsByKey.TryGetValue(key, out var existingJobId) &&
                _jobsById.TryGetValue(existingJobId, out var existingJob) &&
                existingJob.IsActive(now))
            {
                return Task.FromResult(new KeywordGraphJobSubmissionResult(
                    false,
                    $"A keyword job is already running for {projectContext ?? "all-projects"} ({existingJob.Operation}).",
                    existingJob.ToStatus(now)));
            }

            var jobId = Guid.NewGuid();
            var job = new JobRecord(
                jobId,
                operation,
                projectContext,
                now,
                now.Add(leaseTtl),
                "Running");

            _jobsById[jobId] = job;
            _activeJobIdsByKey[key] = jobId;

            _ = RunBackgroundJobAsync(jobId, key, projectContext, runJob, CancellationToken.None);

            return Task.FromResult(new KeywordGraphJobSubmissionResult(
                true,
                $"Started {operation} job {jobId:D}.",
                job.ToStatus(now)));
        }
    }

    private async Task RunBackgroundJobAsync(
        Guid jobId,
        string key,
        string? projectContext,
        Func<IKeywordGraphService, string?, CancellationToken, Task<string>> runJob,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<IKeywordGraphService>();
            var summary = await runJob(service, projectContext, cancellationToken);
            CompleteJob(jobId, key, "Completed", summary, error: null);
        }
        catch (Exception ex)
        {
            CompleteJob(jobId, key, "Failed", summary: null, error: ex.Message);
        }
    }

    private void CompleteJob(Guid jobId, string key, string state, string? summary, string? error)
    {
        lock (_gate)
        {
            if (!_jobsById.TryGetValue(jobId, out var job) || job.State != "Running")
                return;

            _jobsById[jobId] = job with
            {
                State = state,
                CompletedAt = timeProvider.GetUtcNow(),
                Summary = summary,
                Error = error
            };

            if (_activeJobIdsByKey.TryGetValue(key, out var activeJobId) && activeJobId == jobId)
                _activeJobIdsByKey.Remove(key);
        }
    }

    private static string BuildKey(string? projectContext) =>
        projectContext?.Trim() ?? string.Empty;

    private sealed record JobRecord(
        Guid JobId,
        string Operation,
        string? ProjectContext,
        DateTimeOffset StartedAt,
        DateTimeOffset ExpiresAt,
        string State,
        DateTimeOffset? CompletedAt = null,
        string? Summary = null,
        string? Error = null)
    {
        public bool IsActive(DateTimeOffset now) =>
            State == "Running" && now <= ExpiresAt;

        public KeywordGraphJobStatus ToStatus(DateTimeOffset now)
        {
            var state = State == "Running" && now > ExpiresAt
                ? "Expired"
                : State;

            return new KeywordGraphJobStatus(
                JobId,
                Operation,
                ProjectContext,
                state,
                StartedAt,
                ExpiresAt,
                CompletedAt,
                Summary,
                Error);
        }
    }
}
