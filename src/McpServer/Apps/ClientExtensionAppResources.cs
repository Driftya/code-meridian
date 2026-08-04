using System.ComponentModel;
using ModelContextProtocol.Extensions.Apps;
using ModelContextProtocol.Server;

namespace CodeMeridian.McpServer.Apps;

#pragma warning disable MCPEXP003 // MCP Apps is experimental in the 2.0 SDK.
[McpServerResourceType]
public sealed class ClientExtensionAppResources
{
    public const string ResourceUri = "ui://code-meridian/client-extension-contract";

    private static readonly string AppDirectory = System.IO.Path.Combine(AppContext.BaseDirectory, "Apps");

    [McpServerResource(
        UriTemplate = ResourceUri,
        Name = "client-extension-contract-viewer",
        MimeType = McpApps.HtmlMimeType)]
    [McpMeta("ui", JsonValue = """{"csp":{"connectDomains":[],"resourceDomains":[],"frameDomains":[],"baseUriDomains":[]},"prefersBorder":true}""")]
    [Description("Read-only viewer for the CodeMeridian client extension contract")]
    public static string GetClientExtensionContractViewer() =>
        File.ReadAllText(System.IO.Path.Combine(AppDirectory, "client-extension-contract.html"));
}
#pragma warning restore MCPEXP003
