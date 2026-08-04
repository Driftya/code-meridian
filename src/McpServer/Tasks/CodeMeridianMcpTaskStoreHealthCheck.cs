using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CodeMeridian.McpServer.Tasks;

internal sealed class CodeMeridianMcpTaskStoreHealthCheck(CodeMeridianMcpTaskStore store) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyDictionary<string, object> data = new Dictionary<string, object>
        {
            ["activeTasks"] = store.ActiveTaskCount,
            ["maximumActiveTasks"] = store.MaximumActiveTasks,
            ["timeToLiveMinutes"] = store.TimeToLive.TotalMinutes,
            ["maximumResultBytes"] = store.MaximumResultBytes,
            ["storage"] = "process-local-memory"
        };

        return Task.FromResult(HealthCheckResult.Healthy(
            "The process-local MCP task store is available.",
            data));
    }
}
