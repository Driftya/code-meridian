namespace CodeMeridian.McpServer.Tests;

public sealed class LiveFactAttribute : FactAttribute
{
    public LiveFactAttribute(bool requiresMutatingTasks = false)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CODEMERIDIAN_LIVE_URL"))
            || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CODEMERIDIAN_LIVE_API_KEY")))
        {
            Skip = "Set CODEMERIDIAN_LIVE_URL and CODEMERIDIAN_LIVE_API_KEY to run live MCP acceptance tests.";
        }
        else if (requiresMutatingTasks
                 && !string.Equals(
                     Environment.GetEnvironmentVariable("CODEMERIDIAN_LIVE_ENABLE_MUTATING_TASKS"),
                     "true",
                     StringComparison.OrdinalIgnoreCase))
        {
            Skip = "Set CODEMERIDIAN_LIVE_ENABLE_MUTATING_TASKS=true to run live graph-maintenance task tests.";
        }
    }
}
