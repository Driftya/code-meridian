using System.ComponentModel;
using CodeMeridian.Application.Services;
using ModelContextProtocol.Server;

namespace CodeMeridian.McpServer.Tools;

public sealed partial class CodebaseTools
{
    [McpServerTool(Name = "find_config_definitions")]
    [Description(
        "Find where a canonical configuration key is defined or overridden across configuration files. " +
        "Use this when the user asks where a setting comes from, which files override it, or how ':' and '__' forms relate.")]
    public Task<string> FindConfigDefinitionsAsync(
        [Description("Canonical configuration key, e.g. 'Neo4j:Uri' or 'CodeMeridian:Auth:ApiKey'.")]
        string canonicalKey,
        [Description("Optional project name to scope the lookup.")]
        string? projectContext = null,
        CancellationToken cancellationToken = default) =>
        queryService.FindConfigDefinitionsAsync(canonicalKey, projectContext, cancellationToken);

    [McpServerTool(Name = "find_config_usage")]
    [Description(
        "Find code nodes that directly read or bind a canonical configuration key. " +
        "Use this when the user asks what code uses a setting or which options class binds a configuration section.")]
    public Task<string> FindConfigUsageAsync(
        [Description("Canonical configuration key, e.g. 'Neo4j:Uri' or 'CodeMeridian:Auth:ApiKey'.")]
        string canonicalKey,
        [Description("Optional project name to scope the lookup.")]
        string? projectContext = null,
        CancellationToken cancellationToken = default) =>
        queryService.FindConfigUsageAsync(canonicalKey, projectContext, cancellationToken);
}
