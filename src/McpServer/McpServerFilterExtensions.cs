using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using CodeMeridian.McpServer.Configuration;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CodeMeridian.McpServer;

internal static class McpServerFilterExtensions
{
    private const string InstrumentationName = "CodeMeridian.McpServer";
    private static readonly ActivitySource ToolActivitySource = new(InstrumentationName);
    private static readonly Meter ToolMeter = new(InstrumentationName);
    private static readonly Counter<long> ToolCalls = ToolMeter.CreateCounter<long>(
        "codemeridian.mcp.tool.calls",
        description: "MCP tool calls by bounded category and outcome");
    private static readonly Histogram<double> ToolDuration = ToolMeter.CreateHistogram<double>(
        "codemeridian.mcp.tool.duration",
        unit: "ms",
        description: "MCP tool duration by bounded category and outcome");
    private static readonly HashSet<string> MaintenanceTools =
    [
        "rebuild_keyword_graph",
        "classify_keywords"
    ];
    internal static readonly TimeSpan ToolCatalogTimeToLive = TimeSpan.FromMinutes(5);

    public static IMcpServerBuilder WithCodeMeridianFilters(this IMcpServerBuilder builder) =>
        builder.WithRequestFilters(filters =>
        {
            filters.AddListToolsFilter(next => async (request, cancellationToken) =>
            {
                var result = await next(request, cancellationToken);
                result.TimeToLive = ToolCatalogTimeToLive;
                result.CacheScope = CacheScope.Private;
                return result;
            });

            filters.AddCallToolFilter(next => async (request, cancellationToken) =>
            {
                var toolName = request.MatchedPrimitive?.Id ?? "unknown";
                var annotations = (request.MatchedPrimitive as McpServerTool)?.ProtocolTool.Annotations;
                var toolCategory = GetToolCategory(
                    toolName,
                    annotations?.DestructiveHint,
                    annotations?.ReadOnlyHint);
                var logger = request.Services?
                    .GetService<ILoggerFactory>()?
                    .CreateLogger("CodeMeridian.Mcp.Tools");
                var stopwatch = Stopwatch.StartNew();
                var runtimeOptions = request.Services?.GetService<McpTaskRuntimeOptions>()
                    ?? new McpTaskRuntimeOptions();
                using var timeoutSource = toolCategory == "maintenance"
                    ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
                    : null;
                timeoutSource?.CancelAfter(runtimeOptions.MaxDuration);
                var executionToken = timeoutSource?.Token ?? cancellationToken;
                using var activity = ToolActivitySource.StartActivity(
                    "mcp.tools.call",
                    ActivityKind.Server);
                activity?.SetTag("mcp.tool.name", toolName);
                activity?.SetTag("mcp.tool.category", toolCategory);

                try
                {
                    var result = await next(request, executionToken);
                    if (toolCategory == "maintenance"
                        && JsonSerializer.SerializeToUtf8Bytes(result, McpJsonUtilities.DefaultOptions).Length
                            > runtimeOptions.BoundedMaxResultBytes)
                    {
                        Record(toolCategory, "result_too_large", stopwatch.Elapsed.TotalMilliseconds, activity);
                        logger?.LogWarning(
                            "MCP tool {ToolName} exceeded the configured result-size limit after {ElapsedMilliseconds} ms",
                            toolName,
                            stopwatch.ElapsedMilliseconds);
                        return CreateToolError(
                            $"The tool result exceeded the configured {runtimeOptions.BoundedMaxResultBytes}-byte limit.");
                    }

                    var outcome = result.IsError == true ? "tool_error" : "success";
                    Record(toolCategory, outcome, stopwatch.Elapsed.TotalMilliseconds, activity);
                    logger?.LogInformation(
                        "MCP tool {ToolName} completed in {ElapsedMilliseconds} ms with error state {IsError}",
                        toolName,
                        stopwatch.ElapsedMilliseconds,
                        result.IsError ?? false);
                    return result;
                }
                catch (OperationCanceledException) when (
                    timeoutSource?.IsCancellationRequested == true
                    && !cancellationToken.IsCancellationRequested)
                {
                    Record(toolCategory, "timeout", stopwatch.Elapsed.TotalMilliseconds, activity);
                    logger?.LogWarning(
                        "MCP tool {ToolName} exceeded the configured duration limit after {ElapsedMilliseconds} ms",
                        toolName,
                        stopwatch.ElapsedMilliseconds);
                    return CreateToolError(
                        $"The maintenance operation exceeded the configured {runtimeOptions.MaxDuration.TotalSeconds:g}-second duration limit.");
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    Record(toolCategory, "cancelled", stopwatch.Elapsed.TotalMilliseconds, activity);
                    logger?.LogInformation(
                        "MCP tool {ToolName} was cancelled after {ElapsedMilliseconds} ms",
                        toolName,
                        stopwatch.ElapsedMilliseconds);
                    throw;
                }
                catch
                {
                    Record(toolCategory, "failure", stopwatch.Elapsed.TotalMilliseconds, activity);
                    logger?.LogWarning(
                        "MCP tool {ToolName} failed after {ElapsedMilliseconds} ms",
                        toolName,
                        stopwatch.ElapsedMilliseconds);
                    throw;
                }
            });
        });

    private static CallToolResult CreateToolError(string message) =>
        new()
        {
            IsError = true,
            Content = [new TextContentBlock { Text = message }]
        };

    private static string GetToolCategory(
        string toolName,
        bool? destructive,
        bool? readOnly)
    {
        if (toolName == "unknown")
            return "unknown";
        if (destructive == true)
            return "destructive";
        if (MaintenanceTools.Contains(toolName))
            return "maintenance";
        if (readOnly == false)
            return "mutation";
        return "query";
    }

    private static void Record(
        string toolCategory,
        string outcome,
        double elapsedMilliseconds,
        Activity? activity)
    {
        var tags = new TagList
        {
            { "mcp.tool.category", toolCategory },
            { "mcp.tool.outcome", outcome }
        };
        ToolCalls.Add(1, tags);
        ToolDuration.Record(elapsedMilliseconds, tags);
        activity?.SetTag("mcp.tool.outcome", outcome);
        activity?.SetStatus(outcome is "success" or "cancelled"
            ? ActivityStatusCode.Ok
            : ActivityStatusCode.Error);
    }
}
