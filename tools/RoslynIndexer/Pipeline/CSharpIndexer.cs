using CodeMeridian.Core.Knowledge;
using CodeMeridian.Sdk;
using CodeMeridian.Sdk.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;

namespace CodeMeridian.RoslynIndexer.Pipeline;

/// <summary>
/// Walks each .cs file using Roslyn's syntax tree (no compilation needed)
/// and extracts: namespaces, classes, interfaces, enums, methods, properties.
/// Relationships (Contains, Inherits, Implements, Calls, Uses) are also extracted.
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

        var attemptedCallEdges = edges.Count(e => e.RelationshipType == "Calls");
        var attemptedReferenceEdges = edges.Count(e => e.RelationshipType is "Uses" or "Implements" or "Inherits");
        edges = ResolveCallEdges(nodes, edges);
        edges = ResolveReferenceEdges(nodes, edges);
        var resolvedCallEdges = edges.Count(e => e.RelationshipType == "Calls");
        var resolvedReferenceEdges = edges.Count(e => e.RelationshipType is "Uses" or "Implements" or "Inherits");
        if (attemptedCallEdges > 0)
        {
            logger.LogInformation(
                "  Resolved {Resolved}/{Attempted} local call edge(s).",
                resolvedCallEdges,
                attemptedCallEdges);
        }
        if (attemptedReferenceEdges > 0)
        {
            logger.LogInformation(
                "  Resolved {Resolved}/{Attempted} local type reference edge(s).",
                resolvedReferenceEdges,
                attemptedReferenceEdges);
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
        CSharpRouteExtractor.Extract(root, relPath, projectContext, nodes, edges);
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
                    sourceHash: n.SourceHash,
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
                isAsync: e.IsAsync,
                callSite: e.CallSite,
                paramCount: e.ParamCount,
                confidence: e.Confidence,
                cancellationToken: cancellationToken));

            await Task.WhenAll(tasks);
        }
    }

    private static List<IngestEdgeRequest> ResolveCallEdges(
        IReadOnlyList<IngestNodeRequest> nodes,
        IReadOnlyList<IngestEdgeRequest> edges)
    {
        var nodesById = nodes
            .GroupBy(n => n.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var methodCandidates = nodes
            .Where(n => n.Type.Equals("Method", StringComparison.OrdinalIgnoreCase))
            .Select(n => new MethodCandidate(
                n.Id,
                n.Namespace,
                n.FilePath,
                MethodName(n.Name),
                ParameterCount(n.Name)))
            .GroupBy(n => (n.Name, n.ParameterCount), StringTupleComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringTupleComparer.Ordinal);

        var resolved = new List<IngestEdgeRequest>(edges.Count);
        foreach (var edge in edges)
        {
            if (edge.RelationshipType != "Calls" || edge.CallName is null)
            {
                resolved.Add(edge);
                continue;
            }

            if (!nodesById.TryGetValue(edge.SourceId, out var source))
                continue;

            if (edge.ParamCount is null)
                continue;

            if (!methodCandidates.TryGetValue((edge.CallName, edge.ParamCount.Value), out var candidates))
                continue;

            var selected = SelectBestCandidate(source, candidates);
            if (selected is not null)
                resolved.Add(edge with { TargetId = selected.Id });
        }

        return resolved
            .DistinctBy(edge => (edge.SourceId, edge.TargetId, edge.RelationshipType))
            .ToList();
    }

    private static List<IngestEdgeRequest> ResolveReferenceEdges(
        IReadOnlyList<IngestNodeRequest> nodes,
        IReadOnlyList<IngestEdgeRequest> edges)
    {
        var nodesById = nodes
            .GroupBy(n => n.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var typeCandidates = nodes
            .Where(n => n.Type is "Class" or "Interface" or "Enum")
            .Select(n => new TypeCandidate(n.Id, n.Type, n.Namespace, n.FilePath, n.Name, ShortTypeName(n.Id)))
            .GroupBy(n => (n.Type, n.Name), StringTupleComparer.OrdinalType)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringTupleComparer.OrdinalType);

        var resolved = new List<IngestEdgeRequest>(edges.Count);
        foreach (var edge in edges)
        {
            if (edge.RelationshipType is not ("Uses" or "Implements" or "Inherits"))
            {
                resolved.Add(edge);
                continue;
            }

            if (nodesById.ContainsKey(edge.TargetId))
            {
                resolved.Add(edge);
                continue;
            }

            if (!nodesById.TryGetValue(edge.SourceId, out var source) || edge.TargetName is null || edge.TargetType is null)
                continue;

            if (!typeCandidates.TryGetValue((edge.TargetType, edge.TargetName), out var candidates))
                continue;

            var selected = SelectBestTypeCandidate(source, candidates);
            if (selected is not null)
                resolved.Add(edge with { TargetId = selected.Id });
        }

        return resolved
            .Where(edge => !string.IsNullOrWhiteSpace(edge.TargetId))
            .DistinctBy(edge => (edge.SourceId, edge.TargetId, edge.RelationshipType))
            .ToList();
    }

    private static TypeCandidate? SelectBestTypeCandidate(
        IngestNodeRequest source,
        IReadOnlyList<TypeCandidate> candidates)
    {
        if (candidates.Count == 1)
            return candidates[0];

        var sameNamespace = candidates
            .Where(candidate => string.Equals(candidate.Namespace, source.Namespace, StringComparison.Ordinal))
            .ToArray();
        if (sameNamespace.Length == 1)
            return sameNamespace[0];

        var sameFile = candidates
            .Where(candidate => string.Equals(candidate.FilePath, source.FilePath, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return sameFile.Length == 1 ? sameFile[0] : null;
    }

    private static string ShortTypeName(string id)
    {
        var name = id.Split("::").LastOrDefault() ?? id;
        var dot = name.LastIndexOf('.');
        return dot >= 0 ? name[(dot + 1)..] : name;
    }

    private static MethodCandidate? SelectBestCandidate(
        IngestNodeRequest source,
        IReadOnlyList<MethodCandidate> candidates)
    {
        if (candidates.Count == 1)
            return candidates[0];

        var sameFile = candidates
            .Where(candidate => string.Equals(candidate.FilePath, source.FilePath, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (sameFile.Length == 1)
            return sameFile[0];

        var sameNamespace = candidates
            .Where(candidate => string.Equals(candidate.Namespace, source.Namespace, StringComparison.Ordinal))
            .ToArray();
        return sameNamespace.Length == 1 ? sameNamespace[0] : null;
    }

    private static string MethodName(string signature)
    {
        var openParen = signature.IndexOf('(');
        return openParen > 0 ? signature[..openParen] : signature;
    }

    private static int ParameterCount(string signature)
    {
        var openParen = signature.IndexOf('(');
        var closeParen = signature.LastIndexOf(')');
        if (openParen < 0 || closeParen <= openParen + 1)
            return 0;

        return signature[(openParen + 1)..closeParen]
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Length;
    }

    private sealed record MethodCandidate(string Id, string? Namespace, string? FilePath, string Name, int ParameterCount);
    private sealed record TypeCandidate(string Id, string Type, string? Namespace, string? FilePath, string Name, string ShortName);

    private sealed class StringTupleComparer : IEqualityComparer<(string Name, int ParameterCount)>
    {
        public static readonly StringTupleComparer Ordinal = new();
        public static readonly IEqualityComparer<(string Type, string Name)> OrdinalType =
            EqualityComparer<(string Type, string Name)>.Create(
                (x, y) => string.Equals(x.Type, y.Type, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(x.Name, y.Name, StringComparison.Ordinal),
                obj => HashCode.Combine(
                    StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Type),
                    StringComparer.Ordinal.GetHashCode(obj.Name)));

        public bool Equals((string Name, int ParameterCount) x, (string Name, int ParameterCount) y) =>
            x.ParameterCount == y.ParameterCount && string.Equals(x.Name, y.Name, StringComparison.Ordinal);

        public int GetHashCode((string Name, int ParameterCount) obj) =>
            HashCode.Combine(StringComparer.Ordinal.GetHashCode(obj.Name), obj.ParameterCount);
    }
}

// -- Records -------------------------------------------------------------------

internal sealed record IngestNodeRequest(
    string Id, string Name, string Type,
    string? Namespace, string? FilePath, int? LineNumber, string? Summary, int? LineCount = null, string? SourceSnippet = null, string? SourceHash = null);

internal sealed record IngestEdgeRequest(
    string SourceId,
    string TargetId,
    string RelationshipType,
    string? CallName = null,
    int? ParamCount = null,
    string? TargetName = null,
    string? TargetType = null,
    bool? IsAsync = null,
    string? CallSite = null,
    double? Confidence = null);

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
            null, filePath, 1, null, GetLineCount(node), SourceHash: HashSource(node.ToFullString())));

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
            _currentNamespace, filePath, line, summary, lineCount, ExtractSourceSnippet(node), HashSource(node.ToFullString())));

        // Contains edge from namespace
        if (_currentNamespace is not null)
            edges.Add(new IngestEdgeRequest(MakeId("Namespace", _currentNamespace), id, "Contains"));

        // Inherits / Implements edges from base list
        foreach (var baseType in node.BaseList?.Types ?? Enumerable.Empty<BaseTypeSyntax>())
        {
            var baseName = CleanTypeName(baseType.Type.ToString());
            if (baseName is null)
                continue;

            var targetType = baseName.StartsWith('I') ? "Interface" : "Class";
            var baseId = MakeId(targetType, baseName);
            var relType = targetType == "Interface" ? "Implements" : "Inherits";
            edges.Add(new IngestEdgeRequest(id, baseId, relType, TargetName: baseName, TargetType: targetType));
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
            _currentNamespace, filePath, line, summary, lineCount, ExtractSourceSnippet(node), HashSource(node.ToFullString())));

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
            _currentNamespace, filePath, line, null, GetLineCount(node), ExtractSourceSnippet(node), HashSource(node.ToFullString())));

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
        AddParameterTypeUseEdges(node.ParameterList.Parameters, id);
        base.VisitMethodDeclaration(node);
        _currentMemberId = previousMemberId;
    }

    public override void VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
    {
        if (_currentTypeId is null) return;

        var id = AddMethodNode(
            node.Identifier.Text,
            node.ParameterList.Parameters,
            node,
            _currentTypeId,
            summary: null);

        AddParameterTypeUseEdges(node.ParameterList.Parameters, _currentTypeId);
        AddParameterTypeUseEdges(node.ParameterList.Parameters, id);

        var previousMemberId = _currentMemberId;
        _currentMemberId = id;
        base.VisitConstructorDeclaration(node);
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
            _currentNamespace, filePath, line, null, GetLineCount(node), ExtractSourceSnippet(node), HashSource(node.ToFullString())));

        edges.Add(new IngestEdgeRequest(_currentTypeId, id, "Contains"));
        AddTypeUseEdge(id, node.Type?.ToString());
        AddTypeUseEdge(_currentTypeId, node.Type?.ToString());
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
            _currentNamespace, filePath, line, summary, lineCount, ExtractSourceSnippet(node), HashSource(node.ToFullString())));

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
            .Select(invocation => new
            {
                Name = ExtractCalleeName(invocation),
                ParamCount = invocation.ArgumentList.Arguments.Count
            })
            .Where(call => call.Name is not null)
            .Distinct();

        foreach (var callee in invocations)
        {
            edges.Add(new IngestEdgeRequest(
                sourceId,
                TargetId: string.Empty,
                RelationshipType: "Calls",
                CallName: callee.Name,
                ParamCount: callee.ParamCount));
        }
    }

    private void AddParameterTypeUseEdges(SeparatedSyntaxList<ParameterSyntax> parameters, string sourceId)
    {
        foreach (var parameter in parameters)
            AddTypeUseEdge(sourceId, parameter.Type?.ToString());
    }

    private void AddTypeUseEdge(string sourceId, string? rawType)
    {
        var typeName = CleanTypeName(rawType);
        if (typeName is null || IsBuiltInType(typeName))
            return;

        var targetType = typeName.StartsWith('I') ? "Interface" : "Class";
        edges.Add(new IngestEdgeRequest(
            sourceId,
            TargetId: string.Empty,
            RelationshipType: "Uses",
            TargetName: typeName,
            TargetType: targetType));
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

    private static string HashSource(string source)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(source));
        return Convert.ToHexString(bytes).ToLowerInvariant();
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

    private static string? CleanTypeName(string? rawType)
    {
        if (string.IsNullOrWhiteSpace(rawType))
            return null;

        var name = rawType.Trim().TrimEnd('?');
        if (name.EndsWith("[]", StringComparison.Ordinal))
            name = name[..^2];

        var genericStart = name.IndexOf('<');
        if (genericStart >= 0)
            name = name[..genericStart];

        var dot = name.LastIndexOf('.');
        return dot >= 0 ? name[(dot + 1)..] : name;
    }

    private static bool IsBuiltInType(string typeName) =>
        typeName is "string" or "int" or "long" or "short" or "byte" or "bool" or "decimal"
            or "double" or "float" or "object" or "Guid" or "DateTime" or "DateTimeOffset"
            or "CancellationToken" or "Task" or "ValueTask" or "IEnumerable" or "IReadOnlyList"
            or "List" or "Dictionary";
}
