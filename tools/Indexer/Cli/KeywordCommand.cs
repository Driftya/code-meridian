using System.Net.Http.Headers;
using CodeMeridian.Sdk;
using CodeMeridian.Tooling.Configuration;

namespace CodeMeridian.Indexer.Cli.Commands;

internal sealed class KeywordCommand(IToolConfigurationService configurationService)
{
    public async Task<int> RunAsync(KeywordCommandOptions options)
    {
        var context = configurationService.CreateContext(options.Path);
        var codeMeridianUrl = configurationService.ResolveCodeMeridianUrl(context, options.CodeMeridianUrl);
        var project = configurationService.ResolveProject(context, options.Project, includeFallback: false);

        using var httpClient = new HttpClient
        {
            BaseAddress = new Uri(codeMeridianUrl),
            Timeout = TimeSpan.FromMinutes(10)
        };
        httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
        if (!string.IsNullOrWhiteSpace(context.ApiKey))
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", context.ApiKey);

        var client = new CodeMeridianClient(httpClient);

        try
        {
            if (options.Action == KeywordCommandAction.Status)
            {
                if (options.JobId is null)
                {
                    Console.Error.WriteLine("error: --job-id is required for keyword status.");
                    return 1;
                }

                var status = await client.GetKeywordGraphJobStatusAsync(options.JobId.Value);
                if (status is null)
                {
                    Console.Error.WriteLine($"error: keyword job {options.JobId:D} was not found.");
                    return 1;
                }

                PrintJobStatus(status);
                return IsTerminal(status.State) ? (string.Equals(status.State, "Completed", StringComparison.OrdinalIgnoreCase) ? 0 : 1) : 2;
            }

            if (options.Background)
            {
                var ttlSeconds = options.LeaseTtlSeconds ?? 1_800;
                if (options.Action == KeywordCommandAction.Classify)
                {
                    Console.WriteLine($"Starting keyword classification job at {codeMeridianUrl}{(string.IsNullOrWhiteSpace(project) ? " for all projects" : $" for '{project}'")}...");
                    var job = await client.StartClassifyKeywordsAsync(project, ttlSeconds);
                    if (job is null)
                    {
                        Console.Error.WriteLine("error: keyword classification job could not be started.");
                        return 1;
                    }

                    PrintJobSubmission(job);
                    if (options.Wait)
                        return await WaitForJobAsync(client, job.Job.JobId);

                    return job.Accepted ? 0 : 2;
                }

                Console.WriteLine($"Starting keyword rebuild job at {codeMeridianUrl}{(string.IsNullOrWhiteSpace(project) ? " for all projects" : $" for '{project}'")}...");
                var rebuildJob = await client.StartRebuildKeywordGraphAsync(project, ttlSeconds);
                if (rebuildJob is null)
                {
                    Console.Error.WriteLine("error: keyword graph rebuild job could not be started.");
                    return 1;
                }

                PrintJobSubmission(rebuildJob);
                if (options.Wait)
                    return await WaitForJobAsync(client, rebuildJob.Job.JobId);

                return rebuildJob.Accepted ? 0 : 2;
            }

            if (options.Action == KeywordCommandAction.Classify)
            {
                Console.WriteLine($"Classifying keywords at {codeMeridianUrl}{(string.IsNullOrWhiteSpace(project) ? " for all projects" : $" for '{project}'")}...");
                await client.ClassifyKeywordsAsync(project);
                Console.WriteLine(string.IsNullOrWhiteSpace(project)
                    ? "Keywords classified for all indexed projects."
                    : $"Keywords classified for '{project}'.");
                return 0;
            }

            Console.WriteLine($"Rebuilding keyword graph at {codeMeridianUrl}{(string.IsNullOrWhiteSpace(project) ? " for all projects" : $" for '{project}'")}...");
            await client.RebuildKeywordGraphAsync(project);
            Console.WriteLine(string.IsNullOrWhiteSpace(project)
                ? "Keyword graph rebuilt for all indexed projects."
                : $"Keyword graph rebuilt for '{project}'.");
            return 0;
        }
        catch (Exception ex)
        {
            var operation = options.Action == KeywordCommandAction.Classify
                ? "keyword classification"
                : "keyword graph rebuild";
            Console.Error.WriteLine($"error: {operation} failed: {ex.Message}");
            return 1;
        }
    }

    private static void PrintJobSubmission(KeywordGraphJobSubmissionResponse job)
    {
        Console.WriteLine(job.Accepted
            ? $"Job started: {job.Job.Operation} {job.Job.JobId:D} ({job.Job.State})"
            : $"Job busy: {job.Message}");
        Console.WriteLine($"Job id   : {job.Job.JobId:D}");
        Console.WriteLine($"State    : {job.Job.State}");
        if (!string.IsNullOrWhiteSpace(job.Job.ProjectContext))
            Console.WriteLine($"Project  : {job.Job.ProjectContext}");
    }

    private async Task<int> WaitForJobAsync(CodeMeridianClient client, Guid jobId)
    {
        while (true)
        {
            var status = await client.GetKeywordGraphJobStatusAsync(jobId);
            if (status is null)
            {
                Console.Error.WriteLine($"error: keyword job {jobId:D} was not found.");
                return 1;
            }

            if (IsTerminal(status.State))
            {
                PrintJobStatus(status);
                return string.Equals(status.State, "Completed", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
            }

            await Task.Delay(TimeSpan.FromSeconds(2));
        }
    }

    private static void PrintJobStatus(KeywordGraphJobStatusResponse status)
    {
        Console.WriteLine($"Job id   : {status.JobId:D}");
        Console.WriteLine($"Action   : {status.Operation}");
        Console.WriteLine($"State    : {status.State}");
        if (!string.IsNullOrWhiteSpace(status.ProjectContext))
            Console.WriteLine($"Project  : {status.ProjectContext}");
        Console.WriteLine($"Started  : {status.StartedAt:u}");
        Console.WriteLine($"Expires  : {status.ExpiresAt:u}");
        if (!string.IsNullOrWhiteSpace(status.Summary))
            Console.WriteLine(status.Summary);
        if (!string.IsNullOrWhiteSpace(status.Error))
            Console.Error.WriteLine($"error: {status.Error}");
    }

    private static bool IsTerminal(string state) =>
        string.Equals(state, "Completed", StringComparison.OrdinalIgnoreCase)
        || string.Equals(state, "Failed", StringComparison.OrdinalIgnoreCase)
        || string.Equals(state, "Expired", StringComparison.OrdinalIgnoreCase);
}
