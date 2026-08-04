using System.ComponentModel;
using CodeMeridian.Application.Services;
using CodeMeridian.McpServer.Apps;
using ModelContextProtocol.Extensions.Apps;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CodeMeridian.McpServer.Tools;

public sealed partial class CodebaseTools
{
    [McpServerTool(Name = "find_connection", Title = "Find Connection", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ConnectionAnalysisResult))]
#pragma warning disable MCPEXP003 // MCP Apps is experimental in the 2.0 SDK.
    [McpAppUi(
        ResourceUri = ConnectionAppResources.ResourceUri,
        Visibility = [McpUiToolVisibility.Model, McpUiToolVisibility.App])]
#pragma warning restore MCPEXP003
    [Description(
        "Find the shortest path between two code elements in the graph. " +
        "Use this when the user asks how two classes or methods relate to each other, " +
        "or to trace an execution path between a controller and a data layer.")]
    public async Task<CallToolResult> FindConnectionAsync(
        [Description("ID of the starting node, e.g. 'MyNamespace.OrderController.CreateAsync(CreateOrderRequest,CancellationToken)'")]
        string fromId,
        [Description("ID of the destination node, e.g. 'MyNamespace.PaymentGateway.ChargeAsync(decimal,CancellationToken)'")]
        string toId,
        [Description("How much context to return: Summary, Compact, or Full. Defaults to Compact.")]
        ContextDetailLevel detailLevel = ContextDetailLevel.Compact,
        CancellationToken cancellationToken = default)
    {
        var result = await queryService.FindConnectionResultAsync(fromId, toId, cancellationToken);
        return StructuredToolResult.Create(result.ToMarkdown(detailLevel), result);
    }
}
