using System.ComponentModel;
using CodeMeridian.Application.RepositoryConstraints;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CodeMeridian.McpServer.Tools;

[McpServerToolType]
public sealed class RepositoryConstraintTools(RepositoryConstraintQueryService service)
{
    [McpServerTool(Name = "find_repository_constraint_violations", Title = "Find Repository Constraint Violations",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true,
        OutputSchemaType = typeof(RepositoryConstraintQueryResult))]
    [Description("Evaluate explicit placement and Markdown-reference rules using published inventory, generation-bound policy and structural parser facts. Returns per-rule applicability, coverage, reasons, exceptions, and globally bounded source locations. Missing/stale evidence, parser failures and unverified fragments cannot produce clean validation. Requires no callable, namespace, document-search or embedding evidence and never reads or executes local source files.")]
    public async Task<CallToolResult> FindAsync(
        [Description("Required project context whose uploaded inventory and policy should be evaluated.")] string projectContext,
        [Description("Optional repository-relative exact file path; directories/subtrees are not selected.")] string? targetPath = null,
        [Description("Maximum findings across all rules, from 1 to 1000. Per-rule totals and truncation remain explicit.")] int findingLimit = 100,
        CancellationToken cancellationToken = default)
    {
        var result = await service.FindAsync(projectContext, targetPath, findingLimit, cancellationToken);
        return StructuredToolResult.Create(result.ToMarkdown(), result);
    }
}
