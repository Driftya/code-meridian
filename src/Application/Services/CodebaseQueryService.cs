using System.Globalization;
using System.Text;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;
using Microsoft.Extensions.Options;

namespace CodeMeridian.Application.Services;

/// <summary>
/// Pure query service — no LLM inside.
/// Copilot (via MCP) supplies the reasoning; this service supplies the facts.
/// </summary>
public sealed partial class CodebaseQueryService : ICodebaseQueryService
{
    private readonly ICodeGraphRepository codeGraph;
    private readonly IVectorRepository vectorStore;
    private readonly CodebaseAnalysisOptions analysisOptions;

    public CodebaseQueryService(
        ICodeGraphRepository codeGraph,
        IVectorRepository vectorStore)
        : this(codeGraph, vectorStore, Options.Create(new CodebaseAnalysisOptions()))
    {
    }

    public CodebaseQueryService(
        ICodeGraphRepository codeGraph,
        IVectorRepository vectorStore,
        IOptions<CodebaseAnalysisOptions> analysisOptions)
    {
        this.codeGraph = codeGraph;
        this.vectorStore = vectorStore;
        this.analysisOptions = analysisOptions.Value;
    }

    public async Task<string> QueryStructureAsync(
        string query,
        string? projectContext = null,
        CancellationToken cancellationToken = default)
    {
        var nodes = await codeGraph.QueryNodesAsync(
            new CodeGraphQuery
            {
                SemanticQuery = query,
                ProjectContext = projectContext,
                Limit = 20
            }, cancellationToken);

        if (nodes.Count == 0)
            return "No matching code elements found. " +
                   "Ingest your codebase first using the ingest_code_node and ingest_relationship MCP tools, " +
                   "or run the provided indexer script.";

        var summaryTasks = nodes.Take(8)
            .Select(n => codeGraph.GetSubgraphSummaryAsync(n.Id, cancellationToken));

        var summaries = await Task.WhenAll(summaryTasks);

        var sb = new StringBuilder();
        sb.AppendLine($"Found **{nodes.Count}** relevant code elements:\n");

        foreach (var summary in summaries.Where(s => !string.IsNullOrWhiteSpace(s)))
        {
            sb.AppendLine(summary);
            sb.AppendLine("---");
        }

        return sb.ToString();
    }

    public async Task<string> GetOverviewAsync(
        string? projectContext = null,
        CancellationToken cancellationToken = default)
    {
        var (namespaces, classes, interfaces) = await (
            codeGraph.QueryNodesAsync(new CodeGraphQuery { TypeFilter = CodeNodeType.Namespace, ProjectContext = projectContext, Limit = 50 }, cancellationToken),
            codeGraph.QueryNodesAsync(new CodeGraphQuery { TypeFilter = CodeNodeType.Class, ProjectContext = projectContext, Limit = 200 }, cancellationToken),
            codeGraph.QueryNodesAsync(new CodeGraphQuery { TypeFilter = CodeNodeType.Interface, ProjectContext = projectContext, Limit = 100 }, cancellationToken)
        ).WhenAll();

        if (classes.Count == 0 && interfaces.Count == 0)
            return $"No code graph data found{(projectContext is not null ? $" for project '{projectContext}'" : "")}. " +
                   "Run the indexer script or use the ingest_code_node tool to populate the knowledge graph.";

        var sb = new StringBuilder();
        sb.AppendLine($"## Architectural Overview{(projectContext is not null ? $" — {projectContext}" : " — All Projects")}\n");
        sb.AppendLine($"| Element | Count |");
        sb.AppendLine($"|---------|-------|");
        sb.AppendLine($"| Namespaces | {namespaces.Count} |");
        sb.AppendLine($"| Classes | {classes.Count} |");
        sb.AppendLine($"| Interfaces | {interfaces.Count} |");
        sb.AppendLine();

        if (namespaces.Count > 0)
        {
            sb.AppendLine("### Namespaces");
            foreach (var ns in namespaces.Take(20))
                sb.AppendLine($"- `{ns.Name}`");
            sb.AppendLine();
        }

        if (interfaces.Count > 0)
        {
            sb.AppendLine("### Key Interfaces");
            foreach (var iface in interfaces.Take(25))
                sb.AppendLine($"- `{iface.Name}`{(iface.FilePath is not null ? $" — `{iface.FilePath}`" : "")}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    public async Task<string> SearchDocumentationAsync(
        string query,
        string? projectContext = null,
        CancellationToken cancellationToken = default)
    {
        var results = await vectorStore.SearchByTextAsync(query, projectContext, topK: 8, cancellationToken);

        if (results.Count == 0)
            return "No documentation found. Ingest docs via the ingest_document MCP tool or POST /api/v1/knowledge/documents.";

        var sb = new StringBuilder();
        sb.AppendLine($"Found **{results.Count}** relevant documents:\n");

        for (var i = 0; i < results.Count; i++)
        {
            var doc = results[i];
            sb.AppendLine($"### [{i + 1}] {doc.Source ?? "Unknown source"}");
            sb.AppendLine(doc.Content.Length > 600 ? doc.Content[..600] + "…" : doc.Content);
            sb.AppendLine();
        }

        return sb.ToString();
    }

    public async Task<string> FindDiagnosticsAsync(
        string? projectContext = null,
        string? severity = null,
        CancellationToken cancellationToken = default)
    {
        var diagnostics = await codeGraph.FindDiagnosticsAsync(projectContext, severity, cancellationToken);
        return FormatDiagnostics(
            diagnostics,
            $"Diagnostics{(projectContext is not null ? $" — {projectContext}" : "")}");
    }

    public async Task<string> FindDiagnosticsForNodeAsync(
        string nodeId,
        CancellationToken cancellationToken = default)
    {
        var diagnostics = await codeGraph.FindDiagnosticsForNodeAsync(nodeId, cancellationToken);
        return FormatDiagnostics(diagnostics, $"Diagnostics Near `{nodeId}`");
    }

    private static string FormatDiagnostics(IReadOnlyList<CodeNode> diagnostics, string title)
    {
        if (diagnostics.Count == 0)
            return "No indexed diagnostics found. Run the indexer with `--include-diagnostics`.";

        var sb = new StringBuilder();
        sb.AppendLine($"## {title}");
        sb.AppendLine();
        sb.AppendLine($"**{diagnostics.Count}** diagnostics indexed:\n");
        sb.AppendLine("| Severity/Code | Source | File | Line | Message |");
        sb.AppendLine("|---|---|---|---:|---|");

        foreach (var diagnostic in diagnostics.Take(50))
        {
            sb.AppendLine(
                $"| `{diagnostic.Name}` | `{diagnostic.Namespace ?? "unknown"}` | `{diagnostic.FilePath ?? ""}` | {diagnostic.LineNumber?.ToString(CultureInfo.InvariantCulture) ?? ""} | {EscapeTableCell(diagnostic.Summary ?? "")} |");
        }

        return sb.ToString();
    }

    private static string EscapeTableCell(string value) =>
        value.Replace("|", "\\|", StringComparison.Ordinal);
}

// Tuple deconstruction helper to run three tasks in parallel without nesting
file static class TaskExtensions
{
    public static async Task<(T1, T2, T3)> WhenAll<T1, T2, T3>(this (Task<T1> t1, Task<T2> t2, Task<T3> t3) tasks)
    {
        await Task.WhenAll(tasks.t1, tasks.t2, tasks.t3);
        return (tasks.t1.Result, tasks.t2.Result, tasks.t3.Result);
    }
}
