using System.Globalization;
using System.Text;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;
using CodeMeridian.Application.Projects;
using CodeMeridian.Application.Services.ContextWorkflows;
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
    private readonly IEmbeddingProvider embeddingProvider;
    private readonly CodebaseAnalysisOptions defaultAnalysisOptions;
    private readonly IProjectAnalysisOptionsResolver analysisOptionsResolver;
    private readonly IIndexedFileRoleClassifier fileRoleClassifier;
    private readonly IAnalysisProfilePolicy analysisProfilePolicy;
    private readonly ProjectProfileService? projectProfiles;
    private readonly AsyncLocal<ResolvedProjectAnalysisOptions?> currentAnalysisResolution = new();

    private CodebaseAnalysisOptions analysisOptions =>
        currentAnalysisResolution.Value?.Options ?? defaultAnalysisOptions;

    public CodebaseQueryService(
        ICodeGraphRepository codeGraph,
        IVectorRepository vectorStore)
        : this(
            codeGraph,
            vectorStore,
            new NoOpEmbeddingProvider(),
            Options.Create(new CodebaseAnalysisOptions()),
            Options.Create(new CodebaseIndexingOptions()),
            new DefaultAnalysisProfilePolicy(),
            new StaticProjectAnalysisOptionsResolver(new CodebaseAnalysisOptions()))
    {
    }

    public CodebaseQueryService(
        ICodeGraphRepository codeGraph,
        IVectorRepository vectorStore,
        IOptions<CodebaseAnalysisOptions> analysisOptions)
        : this(
            codeGraph,
            vectorStore,
            new NoOpEmbeddingProvider(),
            analysisOptions,
            Options.Create(new CodebaseIndexingOptions()),
            new DefaultAnalysisProfilePolicy(),
            new StaticProjectAnalysisOptionsResolver(analysisOptions.Value))
    {
    }

    public CodebaseQueryService(
        ICodeGraphRepository codeGraph,
        IVectorRepository vectorStore,
        IEmbeddingProvider embeddingProvider)
        : this(
            codeGraph,
            vectorStore,
            embeddingProvider,
            Options.Create(new CodebaseAnalysisOptions()),
            Options.Create(new CodebaseIndexingOptions()),
            new DefaultAnalysisProfilePolicy(),
            new StaticProjectAnalysisOptionsResolver(new CodebaseAnalysisOptions()))
    {
    }

    public CodebaseQueryService(
        ICodeGraphRepository codeGraph,
        IVectorRepository vectorStore,
        IEmbeddingProvider embeddingProvider,
        IOptions<CodebaseAnalysisOptions> analysisOptions,
        IOptions<CodebaseIndexingOptions> indexingOptions,
        IAnalysisProfilePolicy analysisProfilePolicy)
        : this(
            codeGraph,
            vectorStore,
            embeddingProvider,
            analysisOptions,
            indexingOptions,
            analysisProfilePolicy,
            new StaticProjectAnalysisOptionsResolver(analysisOptions.Value))
    {
    }

    public CodebaseQueryService(
        ICodeGraphRepository codeGraph,
        IVectorRepository vectorStore,
        IEmbeddingProvider embeddingProvider,
        IOptions<CodebaseAnalysisOptions> analysisOptions,
        IOptions<CodebaseIndexingOptions> indexingOptions,
        IAnalysisProfilePolicy analysisProfilePolicy,
        IProjectAnalysisOptionsResolver analysisOptionsResolver,
        ProjectProfileService? projectProfiles = null)
    {
        this.codeGraph = codeGraph;
        this.vectorStore = vectorStore;
        this.embeddingProvider = embeddingProvider;
        defaultAnalysisOptions = analysisOptions.Value;
        fileRoleClassifier = new ConfiguredIndexedFileRoleClassifier(indexingOptions);
        this.analysisProfilePolicy = analysisProfilePolicy;
        this.analysisOptionsResolver = analysisOptionsResolver;
        this.projectProfiles = projectProfiles;
    }

    private async Task<string> QueryStructureCoreAsync(
        string query,
        string? projectContext = null,
        CancellationToken cancellationToken = default)
    {
        var structuralResult = await TryQueryStructuralIntentAsync(query, projectContext, cancellationToken);
        if (structuralResult is not null)
            return structuralResult;

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

    private async Task<string> GetOverviewCoreAsync(
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
        CancellationToken cancellationToken = default,
        string? rootPath = null,
        string? targetPath = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scoped = rootPath is not null || targetPath is not null;
        var scope = new DocumentQueryScope(rootPath, targetPath);
        if (scoped && string.IsNullOrWhiteSpace(projectContext))
            throw new ArgumentException("Scoped document search requires an explicit project context.", nameof(projectContext));
        ProjectProfileResult? documentProfile = null;
        ContextWorkflowApplicability? documentApplicability = null;
        string DocumentStatus(string status, string message)
        {
            if (documentApplicability is not null) RecordApplicability("search_documentation", documentApplicability with { Status = status, Reasons = [message], Coverage = status == "ok" ? "complete" : "unknown" });
            return $"Document search status: {status}. Scope: {scope.EvidencePath}. {message}";
        }
        if (projectProfiles is not null)
        {
            documentProfile = await GetWorkflowProfileAsync(projectContext, scope.TargetPath, cancellationToken);
            documentApplicability = ContextWorkflowToolPolicy.Evaluate("search_documentation", documentProfile!, new(DocumentScope: scope));
            if (documentApplicability.Status != "ready") return DocumentStatus(documentApplicability.Status, string.Join(" ", documentApplicability.Reasons));
        }
        IReadOnlyList<KnowledgeDocument> results;
        try
        {
            results = scoped
                ? await vectorStore.SearchByTextInScopeAsync(query, projectContext!, scope, topK: 8, cancellationToken)
                : await vectorStore.SearchByTextAsync(query, projectContext, topK: 8, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && documentProfile is not null)
        {
            return DocumentStatus("failed", "Document search execution failed; no successful results are available.");
        }
        if (scoped)
            results = results.Where(document => scope.Contains(document.Source)
                && string.Equals(document.ProjectContext, projectContext, StringComparison.OrdinalIgnoreCase)).Take(8).ToArray();
        if (documentProfile is not null)
        {
            var accepted = new List<KnowledgeDocument>();
            foreach (var document in results)
            {
                if (string.IsNullOrWhiteSpace(document.Source)) continue;
                string path;
                try { path = CodeMeridian.Core.Projects.ProjectProfilePath.Normalize(document.Source); }
                catch (ArgumentException) { continue; }
                var source = await projectProfiles!.GetAsync(projectContext!, path, cancellationToken);
                if (source.Status != "available" || source.Generation != documentProfile.Generation)
                    return DocumentStatus("stale", "Inventory changed during the query; repeat against current coverage.");
                if (source.Target is { Status: "observed", Kind: "markdown" or "text" }) accepted.Add(document);
            }
            results = accepted;
            var current = await projectProfiles!.GetAsync(projectContext!, scope.TargetPath, cancellationToken);
            if (current.Status != "available" || current.Generation != documentProfile.Generation
                || ContextWorkflowToolPolicy.Evaluate("search_documentation", current, new(DocumentScope: scope)).Status != "ready")
                return DocumentStatus("stale", "Inventory or coverage changed during the query; repeat against current evidence.");
        }

        if (documentApplicability is not null) RecordApplicability("search_documentation", documentApplicability with { Status = "ok" });

        if (results.Count == 0)
            return projectProfiles is null ? "No documentation found. Ingest docs via the ingest_document MCP tool or POST /api/v1/knowledge/documents."
                : DocumentStatus("ok", "No matching documents returned within the accepted inventory and published document coverage.");

        var sb = new StringBuilder();
        if (scoped) sb.AppendLine(DocumentStatus("ok", "Results are confined to the requested document scope."));
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

    private async Task<string> FindDiagnosticsCoreAsync(
        string? projectContext = null,
        string? severity = null,
        CancellationToken cancellationToken = default)
    {
        var diagnostics = await codeGraph.FindDiagnosticsAsync(projectContext, severity, cancellationToken);
        return FormatDiagnostics(
            diagnostics,
            $"Diagnostics{(projectContext is not null ? $" — {projectContext}" : "")}");
    }

    private async Task<string> FindDiagnosticsForNodeCoreAsync(
        string nodeId,
        CancellationToken cancellationToken = default)
    {
        var diagnostics = await codeGraph.FindDiagnosticsForNodeAsync(nodeId, cancellationToken);
        if (diagnostics.Count == 0)
            return $"No diagnostics found in this node's file for `{nodeId}`.";

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
