using System.ComponentModel;
using ModelContextProtocol.Extensions.Apps;
using ModelContextProtocol.Server;

namespace CodeMeridian.McpServer.Apps;

#pragma warning disable MCPEXP003 // MCP Apps is experimental in the 2.0 SDK.
[McpServerResourceType]
public sealed class ConnectionAppResources
{
    public const string ResourceUri = "ui://code-meridian/connection-viewer";

    private static readonly string AppDirectory = System.IO.Path.Combine(AppContext.BaseDirectory, "Apps");

    [McpServerResource(
        UriTemplate = ResourceUri,
        Name = "connection-viewer",
        MimeType = McpApps.HtmlMimeType)]
    [McpMeta("ui", JsonValue = """{"csp":{"connectDomains":[],"resourceDomains":[],"frameDomains":[],"baseUriDomains":[]},"prefersBorder":true}""")]
    [Description("Read-only, accessible viewer for a bounded CodeMeridian graph connection path")]
    public static string GetConnectionViewer() =>
        File.ReadAllText(System.IO.Path.Combine(AppDirectory, "connection-viewer.html"));
}
#pragma warning restore MCPEXP003
