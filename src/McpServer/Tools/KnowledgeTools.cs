using System.ComponentModel;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;
using ModelContextProtocol.Server;

namespace CodeMeridian.McpServer.Tools;

/// <summary>
/// Tools for ingesting codebase knowledge into Neo4j.
/// Run these from your CI/CD pipeline, a build script, or via the Copilot chat.
/// </summary>
[McpServerToolType]
public sealed class KnowledgeTools(
    ICodeGraphRepository codeGraph,
    IVectorRepository vectorStore)
{
    [McpServerTool(Name = "ingest_code_node")]
    [Description(
        "Add a code element (class, method, interface, etc.) to the knowledge graph. " +
        "Call this from an AST parser or build-time indexer to map the codebase into Neo4j.")]
    public async Task<string> IngestCodeNodeAsync(
        [Description("Unique identifier for this node, e.g. 'MyNamespace.UserService.SaveAsync'")]
        string id,
        [Description("Display name of the element")]
        string name,
        [Description("Element type: Namespace, Class, Interface, Method, Property, Field, Enum, File, Module")]
        string type,
        [Description("Namespace path")]
        string? namespacePath = null,
        [Description("Relative file path, e.g. 'src/Services/UserService.cs'")]
        string? filePath = null,
        [Description("Line number within the file")]
        int? lineNumber = null,
        [Description("Total number of lines in this element (used for SRP analysis)")]
        int? lineCount = null,
        [Description("One-sentence summary of what this element does")]
        string? summary = null,
        [Description("Project context name, e.g. 'MyApi'")]
        string? projectContext = null,
        [Description("Optional vector embedding as comma-separated floats (e.g. '0.1,0.23,-0.05,...'). Enables find_similar_nodes.")]
        string? embeddingCsv = null,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.TryParse<CodeNodeType>(type, ignoreCase: true, out var nodeType))
            return $"Unknown node type '{type}'. Valid values: {string.Join(", ", Enum.GetNames<CodeNodeType>())}";

        float[]? embedding = null;
        if (!string.IsNullOrWhiteSpace(embeddingCsv))
        {
            try
            {
                embedding = embeddingCsv.Split(',').Select(s => float.Parse(s.Trim())).ToArray();
            }
            catch
            {
                return "Invalid embeddingCsv format. Expected comma-separated floats, e.g. '0.1,0.23,-0.05'.";
            }
        }

        var node = new CodeNode
        {
            Id = id,
            Name = name,
            Type = nodeType,
            Namespace = namespacePath,
            FilePath = filePath,
            LineNumber = lineNumber,
            LineCount = lineCount,
            Summary = summary,
            ProjectContext = projectContext,
            Embedding = embedding
        };

        await codeGraph.UpsertNodeAsync(node, cancellationToken);
        return $"Node '{name}' ({type}) ingested successfully{(embedding is not null ? $" with {embedding.Length}-dim embedding" : "")}.";
    }

    [McpServerTool(Name = "ingest_relationship")]
    [Description(
        "Add a relationship between two code nodes. " +
        "Valid types: Contains, Calls, Implements, Inherits, Uses, DependsOn, Overrides.")]
    public async Task<string> IngestRelationshipAsync(
        [Description("ID of the source node")]
        string sourceId,
        [Description("ID of the target node")]
        string targetId,
        [Description("Relationship type: Contains, Calls, Implements, Inherits, Uses, DependsOn, Overrides")]
        string relationshipType,
        [Description("Whether the call-site uses await/async (for Calls edges)")]
        bool? isAsync = null,
        [Description("Source location of this call, e.g. 'src/Services/UserService.cs:42'")]
        string? callSite = null,
        [Description("Number of arguments passed at this call-site")]
        int? paramCount = null,
        [Description("Indexer confidence (0–1). Omit for certain edges; use <1.0 for inferred/heuristic edges.")]
        double? confidence = null,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.TryParse<CodeEdgeType>(relationshipType, ignoreCase: true, out var edgeType))
            return $"Unknown relationship type '{relationshipType}'. Valid values: {string.Join(", ", Enum.GetNames<CodeEdgeType>())}";

        var edge = new CodeEdge
        {
            SourceId   = sourceId,
            TargetId   = targetId,
            Type       = edgeType,
            IsAsync    = isAsync,
            CallSite   = callSite,
            ParamCount = paramCount,
            Confidence = confidence
        };

        await codeGraph.UpsertEdgeAsync(edge, cancellationToken);
        return $"Relationship {sourceId} --[{relationshipType}]--> {targetId} ingested{(callSite is not null ? $" (call-site: {callSite})" : "")}.";
    }

    [McpServerTool(Name = "ingest_document")]
    [Description(
        "Ingest a text document (README, ADR, code comment, changelog) so it can be searched. " +
        "The document is stored in Neo4j and indexed for full-text search.")]
    public async Task<string> IngestDocumentAsync(
        [Description("Document content (markdown, plain text, or extracted code comments)")]
        string content,
        [Description("Source path or URL, e.g. 'docs/architecture/auth.md'")]
        string? source = null,
        [Description("Project context name")]
        string? projectContext = null,
        [Description("Optional stable ID — auto-generated if not provided")]
        string? id = null,
        CancellationToken cancellationToken = default)
    {
        var document = new KnowledgeDocument
        {
            Id = id ?? Guid.NewGuid().ToString("N"),
            Content = content,
            Source = source,
            ProjectContext = projectContext
        };

        await vectorStore.UpsertAsync(document, cancellationToken);
        return $"Document from '{source ?? "unknown"}' ingested (id: {document.Id}).";
    }

    [McpServerTool(Name = "clear_project_knowledge")]
    [Description(
        "Remove all knowledge (code nodes, relationships, and documents) for a specific project. " +
        "Use this before re-indexing a project after major structural changes.")]
    public async Task<string> ClearProjectKnowledgeAsync(
        [Description("The project context name to clear")]
        string projectContext,
        CancellationToken cancellationToken = default)
    {
        await Task.WhenAll(
            codeGraph.DeleteProjectAsync(projectContext, cancellationToken),
            vectorStore.DeleteProjectAsync(projectContext, cancellationToken));

        return $"All knowledge for project '{projectContext}' removed from Neo4j.";
    }
}
