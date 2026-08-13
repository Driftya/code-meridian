using System.ComponentModel;
using ModelContextProtocol.Extensions.Apps;
using ModelContextProtocol.Server;

namespace CodeMeridian.McpServer.Apps;

#pragma warning disable MCPEXP003 // MCP Apps is experimental in the 2.0 SDK.
[McpServerResourceType]
public sealed class HumanCognitiveSeedChallengeAppResources
{
    public const string ResourceUri = "ui://code-meridian/change-context-challenge";

    private static readonly string AppDirectory = System.IO.Path.Combine(AppContext.BaseDirectory, "Apps");

    [McpServerResource(
        UriTemplate = ResourceUri,
        Name = "change-context-challenge",
        MimeType = McpApps.HtmlMimeType)]
    [McpMeta("ui", JsonValue = """{"csp":{"connectDomains":[],"resourceDomains":[],"frameDomains":[],"baseUriDomains":[]},"prefersBorder":true}""")]
    [Description("Interactive human-cognitive-seed code challenge with retry feedback and optional change-context notes")]
    public static string GetChangeContextChallenge() =>
        File.ReadAllText(System.IO.Path.Combine(AppDirectory, "change-context-challenge.html"));
}
#pragma warning restore MCPEXP003
