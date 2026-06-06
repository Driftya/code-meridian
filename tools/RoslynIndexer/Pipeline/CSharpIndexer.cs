using CodeMeridian.Core.Knowledge;
using CodeMeridian.Sdk;
using CodeMeridian.Sdk.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;

namespace CodeMeridian.RoslynIndexer.Pipeline;

/// <summary>
/// Walks each .cs file using Roslyn's syntax tree (no compilation needed)
/// and extracts: namespaces, classes, interfaces, enums, methods, properties.
/// Relationships (Contains, Inherits, Implements, Calls) are also extracted.
/// Optionally generates vector embeddings for each node if an embedding provider is available.
/// </summary>
public sealed class CSharpIndexer(
    CodeMeridianClient client,
    ILogger<CSharpIndexer> logger)
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

        // Batch ingest - parallelism capped to avoid overwhelming the server
        await BatchIngestNodesAsync(nodes, projectContext, cancellationToken);
        await BatchIngestEdgesAsync(edges, cancellationToken);

        return new IndexStats(nodes.Count, edges.Count);
    }

    // -- Extraction ------------------------------------------------------------

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

    // -- Batch ingestion -------------------------------------------------------

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

            foreach (var n in batches[i])
            {
                await client.IngestCodeNodeAsync(
                    n.Id, n.Name, n.Type,
                    namespacePath: n.Namespace,
                    filePath: n.FilePath,
                    lineNumber: n.LineNumber,
                    lineCount: n.LineCount,
                    summary: n.Summary,
                    sourceSnippet: n.SourceSnippet,
                    projectContext: projectContext,
                    cancellationToken: cancellationToken);
            }
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

// -- Records -------------------------------------------------------------------

internal sealed record IngestNodeRequest(
    string Id, string Name, string Type,
    string? Namespace, string? FilePath, int? LineNumber, string? Summary, int? LineCount = null, string? SourceSnippet = null);

internal sealed record IngestEdgeRequest(
    string SourceId, string TargetId, string RelationshipType);

// -- Roslyn AST walker ---------------------------------------------------------

internal sealed class CSharpAstWalker(
    string filePath,
    string projectContext,
    List<IngestNodeRequest> nodes,
    List<IngestEdgeRequest> edges) : CSharpSyntaxWalker
{
    private readonly string _fileId = $"{projectContext}::File::{filePath}";
    private string? _currentNamespace;
    private string? _currentTypeId;
    private string? _currentMemberId;

    public override void VisitCompilationUnit(CompilationUnitSyntax node)
    {
        nodes.Add(new IngestNodeRequest(_fileId, Path.GetFileName(filePath), "File",
            null, filePath, 1, null, GetLineCount(node)));

        base.VisitCompilationUnit(node);
    }

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
            _currentNamespace, filePath, line, summary, lineCount, ExtractSourceSnippet(node)));

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
            _currentNamespace, filePath, line, summary, lineCount, ExtractSourceSnippet(node)));

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
            _currentNamespace, filePath, line, null, GetLineCount(node), ExtractSourceSnippet(node)));

        if (_currentNamespace is not null)
            edges.Add(new IngestEdgeRequest(MakeId("Namespace", _currentNamespace), id, "Contains"));
    }

    public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
    {
        if (_currentTypeId is null) return;

        var id = AddMethodNode(
            node.Identifier.Text,
            node.ParameterList.Parameters,
            node,
            _currentTypeId,
            ExtractXmlSummary(node));

        var previousMemberId = _currentMemberId;
        _currentMemberId = id;
        base.VisitMethodDeclaration(node);
        _currentMemberId = previousMemberId;
    }

    public override void VisitLocalFunctionStatement(LocalFunctionStatementSyntax node)
    {
        var containerId = _currentMemberId ?? _currentTypeId ?? _fileId;
        var qualifier = LocalFunctionQualifier(containerId);
        var id = AddMethodNode(
            node.Identifier.Text,
            node.ParameterList.Parameters,
            node,
            containerId,
            summary: null,
            nameQualifier: qualifier);

        var previousMemberId = _currentMemberId;
        _currentMemberId = id;
        base.VisitLocalFunctionStatement(node);
        _currentMemberId = previousMemberId;
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

    // -- Helpers ---------------------------------------------------------------

    private string FullName(string localName) =>
        _currentNamespace is not null ? $"{_currentNamespace}.{localName}" : localName;

    private string MakeId(string type, string name) =>
        $"{projectContext}::{type}::{name}";

    private string AddMethodNode(
        string methodName,
        SeparatedSyntaxList<ParameterSyntax> parameters,
        SyntaxNode node,
        string containerId,
        string? summary,
        string? nameQualifier = null)
    {
        var signature = BuildSignature(methodName, parameters);
        var fullName = nameQualifier is not null
            ? $"{nameQualifier}::{signature}"
            : FullName(signature);
        var id = MakeId("Method", fullName);
        var span = node.GetLocation().GetLineSpan();
        var line = span.StartLinePosition.Line + 1;
        var lineCount = span.EndLinePosition.Line - span.StartLinePosition.Line + 1;

        nodes.Add(new IngestNodeRequest(id, signature, "Method",
            _currentNamespace, filePath, line, summary, lineCount, ExtractSourceSnippet(node)));

        edges.Add(new IngestEdgeRequest(containerId, id, "Contains"));
        AddInvocationEdges(node, id);

        return id;
    }

    private string LocalFunctionQualifier(string containerId)
    {
        var projectPrefix = $"{projectContext}::";
        return containerId.StartsWith(projectPrefix, StringComparison.Ordinal)
            ? containerId[projectPrefix.Length..]
            : containerId;
    }

    private void AddInvocationEdges(SyntaxNode node, string sourceId)
    {
        var invocations = node.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Select(ExtractCalleeName)
            .Where(n => n is not null)
            .Distinct();

        foreach (var callee in invocations)
        {
            var calleeId = MakeId("Method", callee!);
            edges.Add(new IngestEdgeRequest(sourceId, calleeId, "Calls"));
        }
    }

    private static string BuildSignature(string methodName, SeparatedSyntaxList<ParameterSyntax> parameters)
    {
        var paramTypes = string.Join(",", parameters.Select(p => p.Type?.ToString() ?? "?"));
        return $"{methodName}({paramTypes})";
    }

    private static int GetLineCount(SyntaxNode node)
    {
        var span = node.GetLocation().GetLineSpan();
        return span.EndLinePosition.Line - span.StartLinePosition.Line + 1;
    }

    private static string? ExtractSourceSnippet(SyntaxNode node)
    {
        const int maxLines = 80;
        const int maxChars = 12_000;

        var text = node.ToFullString().TrimEnd();
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var lines = text.Split(["\r\n", "\n"], StringSplitOptions.None);
        var snippet = string.Join(Environment.NewLine, lines.Take(maxLines));
        return snippet.Length > maxChars ? snippet[..maxChars] : snippet;
    }

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
