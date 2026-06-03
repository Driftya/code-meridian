using CodeMeridian.Sdk;
using CodeMeridian.Sdk.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;

namespace CodeMeridian.Indexer.Pipeline;

/// <summary>
/// Walks each .cs file using Roslyn's syntax tree (no compilation needed)
/// and extracts: namespaces, classes, interfaces, enums, methods, properties.
/// Relationships (Contains, Inherits, Implements, Calls) are also extracted.
/// </summary>
public sealed class CSharpIndexer(CodeMeridianClient client, ILogger<CSharpIndexer> logger)
{
    public async Task<IndexStats> IndexAsync(
        FileInfo[] files,
        string projectContext,
        string rootPath,
        CancellationToken cancellationToken = default)
    {
        var nodes = new List<IngestNodeRequest>();
        var edges = new List<IngestEdgeRequest>();

        foreach (var file in files)
        {
            try
            {
                ExtractFromFile(file, rootPath, projectContext, nodes, edges);
            }
            catch (Exception ex)
            {
                logger.LogWarning("Skipped {File}: {Error}", file.Name, ex.Message);
            }
        }

        // Batch ingest — parallelism capped to avoid overwhelming the server
        await BatchIngestNodesAsync(nodes, projectContext, cancellationToken);
        await BatchIngestEdgesAsync(edges, cancellationToken);

        return new IndexStats(nodes.Count, edges.Count);
    }

    // ── Extraction ────────────────────────────────────────────────────────────

    private static void ExtractFromFile(
        FileInfo file,
        string rootPath,
        string projectContext,
        List<IngestNodeRequest> nodes,
        List<IngestEdgeRequest> edges)
    {
        var source = File.ReadAllText(file.FullName);
        var tree = CSharpSyntaxTree.ParseText(source, path: file.FullName);
        var root = tree.GetCompilationUnitRoot();

        var relPath = Path.GetRelativePath(rootPath, file.FullName).Replace('\\', '/');
        var walker = new CSharpAstWalker(relPath, projectContext, nodes, edges);
        walker.Visit(root);
    }

    // ── Batch ingestion ───────────────────────────────────────────────────────

    private async Task BatchIngestNodesAsync(
        List<IngestNodeRequest> nodes,
        string projectContext,
        CancellationToken cancellationToken)
    {
        const int batchSize = 50;
        var batches = nodes.Chunk(batchSize).ToArray();

        for (var i = 0; i < batches.Length; i++)
        {
            logger.LogInformation(
                "  Ingesting nodes batch {Current}/{Total}...", i + 1, batches.Length);

            var tasks = batches[i].Select(n => client.IngestCodeNodeAsync(
                n.Id, n.Name, n.Type,
                namespacePath: n.Namespace,
                filePath: n.FilePath,
                lineNumber: n.LineNumber,
                lineCount: n.LineCount,
                summary: n.Summary,
                projectContext: projectContext,
                cancellationToken: cancellationToken));

            await Task.WhenAll(tasks);
        }
    }

    private async Task BatchIngestEdgesAsync(
        List<IngestEdgeRequest> edges,
        CancellationToken cancellationToken)
    {
        const int batchSize = 100;
        var batches = edges.Chunk(batchSize).ToArray();

        for (var i = 0; i < batches.Length; i++)
        {
            logger.LogInformation(
                "  Ingesting edges batch {Current}/{Total}...", i + 1, batches.Length);

            var tasks = batches[i].Select(e => client.IngestRelationshipAsync(
                e.SourceId, e.TargetId, e.RelationshipType,
                cancellationToken: cancellationToken));

            await Task.WhenAll(tasks);
        }
    }
}

// ── Records ───────────────────────────────────────────────────────────────────

internal sealed record IngestNodeRequest(
    string Id, string Name, string Type,
    string? Namespace, string? FilePath, int? LineNumber, string? Summary, int? LineCount = null);

internal sealed record IngestEdgeRequest(
    string SourceId, string TargetId, string RelationshipType);

// ── Roslyn AST walker ─────────────────────────────────────────────────────────

internal sealed class CSharpAstWalker(
    string filePath,
    string projectContext,
    List<IngestNodeRequest> nodes,
    List<IngestEdgeRequest> edges) : CSharpSyntaxWalker
{
    private string? _currentNamespace;
    private string? _currentTypeId;

    public override void VisitNamespaceDeclaration(NamespaceDeclarationSyntax node)
    {
        _currentNamespace = node.Name.ToString();
        var id = MakeId("Namespace", _currentNamespace);
        nodes.Add(new IngestNodeRequest(id, _currentNamespace, "Namespace",
            null, filePath, node.GetLocation().GetLineSpan().StartLinePosition.Line + 1, null));
        base.VisitNamespaceDeclaration(node);
    }

    public override void VisitFileScopedNamespaceDeclaration(FileScopedNamespaceDeclarationSyntax node)
    {
        _currentNamespace = node.Name.ToString();
        var id = MakeId("Namespace", _currentNamespace);
        nodes.Add(new IngestNodeRequest(id, _currentNamespace, "Namespace",
            null, filePath, node.GetLocation().GetLineSpan().StartLinePosition.Line + 1, null));
        base.VisitFileScopedNamespaceDeclaration(node);
    }

    public override void VisitClassDeclaration(ClassDeclarationSyntax node)
    {
        var fullName = FullName(node.Identifier.Text);
        var id = MakeId("Class", fullName);
        var span = node.GetLocation().GetLineSpan();
        var line = span.StartLinePosition.Line + 1;
        var lineCount = span.EndLinePosition.Line - span.StartLinePosition.Line + 1;
        var summary = ExtractXmlSummary(node);

        nodes.Add(new IngestNodeRequest(id, node.Identifier.Text, "Class",
            _currentNamespace, filePath, line, summary, lineCount));

        // Contains edge from namespace
        if (_currentNamespace is not null)
            edges.Add(new IngestEdgeRequest(MakeId("Namespace", _currentNamespace), id, "Contains"));

        // Inherits / Implements edges from base list
        foreach (var baseType in node.BaseList?.Types ?? Enumerable.Empty<BaseTypeSyntax>())
        {
            var baseName = baseType.Type.ToString().Split('<')[0]; // strip generics
            var baseId = MakeId(baseName.StartsWith('I') ? "Interface" : "Class", baseName);
            var relType = baseName.StartsWith('I') ? "Implements" : "Inherits";
            edges.Add(new IngestEdgeRequest(id, baseId, relType));
        }

        var prev = _currentTypeId;
        _currentTypeId = id;
        base.VisitClassDeclaration(node);
        _currentTypeId = prev;
    }

    public override void VisitInterfaceDeclaration(InterfaceDeclarationSyntax node)
    {
        var fullName = FullName(node.Identifier.Text);
        var id = MakeId("Interface", fullName);
        var span = node.GetLocation().GetLineSpan();
        var line = span.StartLinePosition.Line + 1;
        var lineCount = span.EndLinePosition.Line - span.StartLinePosition.Line + 1;
        var summary = ExtractXmlSummary(node);

        nodes.Add(new IngestNodeRequest(id, node.Identifier.Text, "Interface",
            _currentNamespace, filePath, line, summary, lineCount));

        if (_currentNamespace is not null)
            edges.Add(new IngestEdgeRequest(MakeId("Namespace", _currentNamespace), id, "Contains"));

        var prev = _currentTypeId;
        _currentTypeId = id;
        base.VisitInterfaceDeclaration(node);
        _currentTypeId = prev;
    }

    public override void VisitEnumDeclaration(EnumDeclarationSyntax node)
    {
        var fullName = FullName(node.Identifier.Text);
        var id = MakeId("Enum", fullName);
        var line = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

        nodes.Add(new IngestNodeRequest(id, node.Identifier.Text, "Enum",
            _currentNamespace, filePath, line, null));

        if (_currentNamespace is not null)
            edges.Add(new IngestEdgeRequest(MakeId("Namespace", _currentNamespace), id, "Contains"));
    }

    public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
    {
        if (_currentTypeId is null) return;

        var methodName = node.Identifier.Text;
        var paramTypes = string.Join(",", node.ParameterList.Parameters
            .Select(p => p.Type?.ToString() ?? "?"));
        var signature = $"{methodName}({paramTypes})";
        var fullName = FullName(signature);
        var id = MakeId("Method", fullName);
        var methodSpan = node.GetLocation().GetLineSpan();
        var line = methodSpan.StartLinePosition.Line + 1;
        var lineCount = methodSpan.EndLinePosition.Line - methodSpan.StartLinePosition.Line + 1;
        var summary = ExtractXmlSummary(node);

        nodes.Add(new IngestNodeRequest(id, signature, "Method",
            _currentNamespace, filePath, line, summary, lineCount));

        edges.Add(new IngestEdgeRequest(_currentTypeId, id, "Contains"));

        // Extract method calls (invocation expressions)
        var invocations = node.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Select(i => ExtractCalleeName(i))
            .Where(n => n is not null)
            .Distinct();

        foreach (var callee in invocations)
        {
            // We create a placeholder target node id; will be resolved on next index run
            var calleeId = MakeId("Method", callee!);
            edges.Add(new IngestEdgeRequest(id, calleeId, "Calls"));
        }
    }

    public override void VisitPropertyDeclaration(PropertyDeclarationSyntax node)
    {
        if (_currentTypeId is null) return;

        var fullName = FullName(node.Identifier.Text);
        var id = MakeId("Property", fullName);
        var line = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

        nodes.Add(new IngestNodeRequest(id, node.Identifier.Text, "Property",
            _currentNamespace, filePath, line, null));

        edges.Add(new IngestEdgeRequest(_currentTypeId, id, "Contains"));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private string FullName(string localName) =>
        _currentNamespace is not null ? $"{_currentNamespace}.{localName}" : localName;

    private string MakeId(string type, string name) =>
        $"{projectContext}::{type}::{name}";

    private static string? ExtractXmlSummary(SyntaxNode node)
    {
        var trivia = node.GetLeadingTrivia()
            .Where(t => t.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) ||
                        t.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia))
            .Select(t => t.ToString())
            .FirstOrDefault();

        if (trivia is null) return null;

        // Extract the text inside <summary>...</summary>
        var start = trivia.IndexOf("<summary>", StringComparison.Ordinal);
        var end = trivia.IndexOf("</summary>", StringComparison.Ordinal);
        if (start < 0 || end < 0) return null;

        var raw = trivia[(start + 9)..end];

        // Strip leading /// and whitespace from each line
        return string.Join(" ", raw
            .Split('\n')
            .Select(l => l.TrimStart().TrimStart('/', ' ').Trim())
            .Where(l => l.Length > 0));
    }

    private static string? ExtractCalleeName(InvocationExpressionSyntax invocation) =>
        invocation.Expression switch
        {
            MemberAccessExpressionSyntax m => m.Name.Identifier.Text,
            IdentifierNameSyntax i => i.Identifier.Text,
            _ => null
        };
}
