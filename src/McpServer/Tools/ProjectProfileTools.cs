using System.ComponentModel;
using CodeMeridian.Application.Projects;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CodeMeridian.McpServer.Tools;

[McpServerToolType]
public sealed class ProjectProfileTools(ProjectProfileService service)
{
    [McpServerTool(Name = "get_project_profile", Title = "Get Project Profile", ReadOnly = true,
        Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true,
        OutputSchemaType = typeof(ProjectProfileResult))]
    [Description("Read a published project discovery profile before choosing tools. Separates observed file kinds, analyzer support and configuration from semantic indexing readiness. Missing profiles are unknown. The server uses uploaded inventory and does not inspect the local filesystem.")]
    public async Task<CallToolResult> GetProjectProfileAsync(
        [Description("Required project context whose uploaded profile should be read.")] string projectContext,
        [Description("Optional repository-relative file path to look up in the complete published inventory.")] string? targetPath = null,
        CancellationToken cancellationToken = default)
    {
        var result = await service.GetAsync(projectContext, targetPath, cancellationToken);
        return StructuredToolResult.Create(result.ToMarkdown(), result);
    }
}
