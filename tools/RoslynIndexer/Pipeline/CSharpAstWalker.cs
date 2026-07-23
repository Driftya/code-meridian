using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp;
using System.Security.Cryptography;
using System.Text;

namespace CodeMeridian.RoslynIndexer.Pipeline;

internal sealed class CSharpAstWalker(
    string filePath,
    string projectContext,
    List<IngestNodeRequest> nodes,
    List<IngestEdgeRequest> edges) : CSharpSyntaxWalker
{
    private readonly string _fileId = $"{projectContext}::File::{filePath}";
    private readonly Stack<Dictionary<string, string>> _identifierTypeScopes = [];
    private string? _currentNamespace;
    private string? _currentTypeId;
    private string? _currentMemberId;
    private Dictionary<string, string> _currentTypeMemberTypes = new(StringComparer.Ordinal);

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
        var id = AddTypeNode("Class", node.Identifier.Text, node, ExtractXmlSummary(node), node.BaseList);
        var prev = _currentTypeId;
        var previousTypeMembers = _currentTypeMemberTypes;
        _currentTypeId = id;
        _currentTypeMemberTypes = BuildTypeMemberTypeMap(node.ParameterList?.Parameters);
        AddParameterTypeUseEdges(node.ParameterList?.Parameters ?? default, id);
        base.VisitClassDeclaration(node);
        _currentTypeId = prev;
        _currentTypeMemberTypes = previousTypeMembers;
    }

    public override void VisitInterfaceDeclaration(InterfaceDeclarationSyntax node)
    {
        var id = AddTypeNode("Interface", node.Identifier.Text, node, ExtractXmlSummary(node), node.BaseList);
        var prev = _currentTypeId;
        var previousTypeMembers = _currentTypeMemberTypes;
        _currentTypeId = id;
        _currentTypeMemberTypes = new Dictionary<string, string>(StringComparer.Ordinal);
        base.VisitInterfaceDeclaration(node);
        _currentTypeId = prev;
        _currentTypeMemberTypes = previousTypeMembers;
    }

    public override void VisitStructDeclaration(StructDeclarationSyntax node)
    {
        var id = AddTypeNode("Struct", node.Identifier.Text, node, ExtractXmlSummary(node), node.BaseList);
        var prev = _currentTypeId;
        var previousTypeMembers = _currentTypeMemberTypes;
        _currentTypeId = id;
        _currentTypeMemberTypes = BuildTypeMemberTypeMap(node.ParameterList?.Parameters);
        AddParameterTypeUseEdges(node.ParameterList?.Parameters ?? default, id);
        base.VisitStructDeclaration(node);
        _currentTypeId = prev;
        _currentTypeMemberTypes = previousTypeMembers;
    }

    public override void VisitRecordDeclaration(RecordDeclarationSyntax node)
    {
        var recordKind = node.ClassOrStructKeyword.IsKind(SyntaxKind.StructKeyword) ? "Struct" : "Class";
        var id = AddTypeNode(recordKind, node.Identifier.Text, node, ExtractXmlSummary(node), node.BaseList);
        var prev = _currentTypeId;
        var previousTypeMembers = _currentTypeMemberTypes;
        _currentTypeId = id;
        _currentTypeMemberTypes = BuildTypeMemberTypeMap(node.ParameterList?.Parameters);
        AddParameterTypeUseEdges(node.ParameterList?.Parameters ?? default, id);
        base.VisitRecordDeclaration(node);
        _currentTypeId = prev;
        _currentTypeMemberTypes = previousTypeMembers;
    }

    public override void VisitEnumDeclaration(EnumDeclarationSyntax node)
    {
        AddTypeNode("Enum", node.Identifier.Text, node, summary: null);
    }

    public override void VisitDelegateDeclaration(DelegateDeclarationSyntax node)
    {
        var id = AddTypeNode("Delegate", node.Identifier.Text, node, ExtractXmlSummary(node));
        AddTypeUseEdge(id, node.ReturnType?.ToString());
        AddParameterTypeUseEdges(node.ParameterList.Parameters, id);
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
        PushIdentifierScope(node.ParameterList.Parameters);
        AddParameterTypeUseEdges(node.ParameterList.Parameters, id);
        try
        {
            base.VisitMethodDeclaration(node);
            AddInvocationEdges(node, id);
        }
        finally
        {
            PopIdentifierScope();
            _currentMemberId = previousMemberId;
        }
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
        PushIdentifierScope(node.ParameterList.Parameters);
        try
        {
            base.VisitConstructorDeclaration(node);
            AddInvocationEdges(node, id);
        }
        finally
        {
            PopIdentifierScope();
            _currentMemberId = previousMemberId;
        }
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
        PushIdentifierScope(node.ParameterList.Parameters);
        AddParameterTypeUseEdges(node.ParameterList.Parameters, id);
        try
        {
            base.VisitLocalFunctionStatement(node);
            AddInvocationEdges(node, id);
        }
        finally
        {
            PopIdentifierScope();
            _currentMemberId = previousMemberId;
        }
    }

    public override void VisitPropertyDeclaration(PropertyDeclarationSyntax node)
    {
        if (_currentTypeId is null) return;

        var id = AddMemberNode("Property", node.Identifier.Text, node, _currentTypeId, summary: null);
        AddTypeUseEdge(id, node.Type?.ToString());
        AddTypeUseEdge(_currentTypeId, node.Type?.ToString());
        RememberType(_currentTypeMemberTypes, node.Identifier.Text, node.Type?.ToString());

        var previousMemberId = _currentMemberId;
        _currentMemberId = id;
        base.VisitPropertyDeclaration(node);
        _currentMemberId = previousMemberId;
    }

    public override void VisitFieldDeclaration(FieldDeclarationSyntax node)
    {
        if (_currentTypeId is null) return;

        foreach (var variable in node.Declaration.Variables)
        {
            var id = AddMemberNode("Field", variable.Identifier.Text, node, _currentTypeId, summary: null);
            AddTypeUseEdge(id, node.Declaration.Type?.ToString());
            AddTypeUseEdge(_currentTypeId, node.Declaration.Type?.ToString());
            RememberType(_currentTypeMemberTypes, variable.Identifier.Text, node.Declaration.Type?.ToString());
        }

        base.VisitFieldDeclaration(node);
    }

    public override void VisitEventFieldDeclaration(EventFieldDeclarationSyntax node)
    {
        if (_currentTypeId is null) return;

        foreach (var variable in node.Declaration.Variables)
        {
            var id = AddMemberNode("Event", variable.Identifier.Text, node, _currentTypeId, summary: null);
            AddTypeUseEdge(id, node.Declaration.Type?.ToString());
            AddTypeUseEdge(_currentTypeId, node.Declaration.Type?.ToString());
            RememberType(_currentTypeMemberTypes, variable.Identifier.Text, node.Declaration.Type?.ToString());
        }

        base.VisitEventFieldDeclaration(node);
    }

    public override void VisitEventDeclaration(EventDeclarationSyntax node)
    {
        if (_currentTypeId is null) return;

        var id = AddMemberNode("Event", node.Identifier.Text, node, _currentTypeId, summary: null);
        AddTypeUseEdge(id, node.Type?.ToString());
        AddTypeUseEdge(_currentTypeId, node.Type?.ToString());
        RememberType(_currentTypeMemberTypes, node.Identifier.Text, node.Type?.ToString());

        var previousMemberId = _currentMemberId;
        _currentMemberId = id;
        base.VisitEventDeclaration(node);
        _currentMemberId = previousMemberId;
    }

    public override void VisitIndexerDeclaration(IndexerDeclarationSyntax node)
    {
        if (_currentTypeId is null) return;

        var id = AddMethodLikeNode(
            "Indexer",
            "this",
            node.ParameterList.Parameters,
            node,
            _currentTypeId,
            ExtractXmlSummary(node));
        AddTypeUseEdge(id, node.Type?.ToString());
        AddTypeUseEdge(_currentTypeId, node.Type?.ToString());
        AddParameterTypeUseEdges(node.ParameterList.Parameters, id);

        var previousMemberId = _currentMemberId;
        _currentMemberId = id;
        PushIdentifierScope(node.ParameterList.Parameters);
        try
        {
            base.VisitIndexerDeclaration(node);
            AddInvocationEdges(node, id);
        }
        finally
        {
            PopIdentifierScope();
            _currentMemberId = previousMemberId;
        }
    }

    public override void VisitLocalDeclarationStatement(LocalDeclarationStatementSyntax node)
    {
        if (_identifierTypeScopes.Count > 0)
        {
            foreach (var variable in node.Declaration.Variables)
                RememberType(_identifierTypeScopes.Peek(), variable.Identifier.Text, ResolveVariableType(node.Declaration.Type, variable.Initializer?.Value));
        }

        base.VisitLocalDeclarationStatement(node);
    }

    public override void VisitOperatorDeclaration(OperatorDeclarationSyntax node)
    {
        if (_currentTypeId is null) return;

        var id = AddMethodLikeNode(
            "Operator",
            $"operator {node.OperatorToken.Text}",
            node.ParameterList.Parameters,
            node,
            _currentTypeId,
            ExtractXmlSummary(node));
        AddTypeUseEdge(id, node.ReturnType?.ToString());
        AddTypeUseEdge(_currentTypeId, node.ReturnType?.ToString());
        AddParameterTypeUseEdges(node.ParameterList.Parameters, id);

        var previousMemberId = _currentMemberId;
        _currentMemberId = id;
        PushIdentifierScope(node.ParameterList.Parameters);
        try
        {
            base.VisitOperatorDeclaration(node);
            AddInvocationEdges(node, id);
        }
        finally
        {
            PopIdentifierScope();
            _currentMemberId = previousMemberId;
        }
    }

    public override void VisitConversionOperatorDeclaration(ConversionOperatorDeclarationSyntax node)
    {
        if (_currentTypeId is null) return;

        var id = AddMethodLikeNode(
            "Operator",
            $"operator {node.ImplicitOrExplicitKeyword.Text}",
            node.ParameterList.Parameters,
            node,
            _currentTypeId,
            ExtractXmlSummary(node));
        AddTypeUseEdge(id, node.Type?.ToString());
        AddTypeUseEdge(_currentTypeId, node.Type?.ToString());
        AddParameterTypeUseEdges(node.ParameterList.Parameters, id);

        var previousMemberId = _currentMemberId;
        _currentMemberId = id;
        PushIdentifierScope(node.ParameterList.Parameters);
        try
        {
            base.VisitConversionOperatorDeclaration(node);
            AddInvocationEdges(node, id);
        }
        finally
        {
            PopIdentifierScope();
            _currentMemberId = previousMemberId;
        }
    }

    private string FullName(string localName) =>
        _currentNamespace is not null ? $"{_currentNamespace}.{localName}" : localName;

    private string MakeId(string type, string name) =>
        $"{projectContext}::{type}::{name}";

    private string AddTypeNode(
        string type,
        string localName,
        SyntaxNode node,
        string? summary,
        BaseListSyntax? baseList = null)
    {
        var fullName = FullName(localName);
        var id = MakeId(type, fullName);
        var span = node.GetLocation().GetLineSpan();
        var line = span.StartLinePosition.Line + 1;
        var lineCount = span.EndLinePosition.Line - span.StartLinePosition.Line + 1;

        nodes.Add(new IngestNodeRequest(id, localName, type,
            _currentNamespace, filePath, line, summary, lineCount, ExtractSourceSnippet(node), HashSource(node.ToFullString())));

        if (_currentNamespace is not null)
            edges.Add(new IngestEdgeRequest(MakeId("Namespace", _currentNamespace), id, "Contains"));

        foreach (var baseType in baseList?.Types ?? Enumerable.Empty<BaseTypeSyntax>())
        {
            var baseName = CleanTypeName(baseType.Type.ToString());
            if (baseName is null)
                continue;

            var targetType = InferTargetType(baseName);
            var relType = targetType == "Interface" ? "Implements" : "Inherits";
            edges.Add(new IngestEdgeRequest(id, MakeId(targetType, baseName), relType, TargetName: baseName, TargetType: targetType));
        }

        return id;
    }

    private string AddMemberNode(
        string memberType,
        string memberName,
        SyntaxNode node,
        string containerId,
        string? summary)
    {
        var fullName = BuildMemberFullName(memberName, containerId);
        var id = MakeId(memberType, fullName);
        var span = node.GetLocation().GetLineSpan();
        var line = span.StartLinePosition.Line + 1;
        var lineCount = span.EndLinePosition.Line - span.StartLinePosition.Line + 1;
        var properties = BuildMemberProperties(containerId);

        nodes.Add(new IngestNodeRequest(id, memberName, memberType,
            _currentNamespace, filePath, line, summary, lineCount, ExtractSourceSnippet(node), HashSource(node.ToFullString()), properties));
        edges.Add(new IngestEdgeRequest(containerId, id, "Contains"));
        AddInvocationEdges(node, id);

        return id;
    }

    private string AddMethodNode(
        string methodName,
        SeparatedSyntaxList<ParameterSyntax> parameters,
        SyntaxNode node,
        string containerId,
        string? summary,
        string? nameQualifier = null)
    {
        var signature = BuildSignature(methodName, parameters);
        var fullName = BuildMemberFullName(signature, containerId, nameQualifier);
        var id = MakeId("Method", fullName);
        var span = node.GetLocation().GetLineSpan();
        var line = span.StartLinePosition.Line + 1;
        var lineCount = span.EndLinePosition.Line - span.StartLinePosition.Line + 1;
        var properties = BuildMemberProperties(containerId, parameters);

        nodes.Add(new IngestNodeRequest(id, signature, "Method",
            _currentNamespace, filePath, line, summary, lineCount, ExtractSourceSnippet(node), HashSource(node.ToFullString()), properties));

        edges.Add(new IngestEdgeRequest(containerId, id, "Contains"));

        return id;
    }

    private string AddMethodLikeNode(
        string memberType,
        string memberName,
        SeparatedSyntaxList<ParameterSyntax> parameters,
        SyntaxNode node,
        string containerId,
        string? summary,
        string? nameQualifier = null)
    {
        var signature = BuildSignature(memberName, parameters);
        var fullName = BuildMemberFullName(signature, containerId, nameQualifier);
        var id = MakeId(memberType, fullName);
        var span = node.GetLocation().GetLineSpan();
        var line = span.StartLinePosition.Line + 1;
        var lineCount = span.EndLinePosition.Line - span.StartLinePosition.Line + 1;
        var properties = BuildMemberProperties(containerId, parameters);

        nodes.Add(new IngestNodeRequest(id, signature, memberType,
            _currentNamespace, filePath, line, summary, lineCount, ExtractSourceSnippet(node), HashSource(node.ToFullString()), properties));

        edges.Add(new IngestEdgeRequest(containerId, id, "Contains"));

        return id;
    }

    private string LocalFunctionQualifier(string containerId)
    {
        var projectPrefix = $"{projectContext}::";
        return containerId.StartsWith(projectPrefix, StringComparison.Ordinal)
            ? containerId[projectPrefix.Length..]
            : containerId;
    }

    private static string BuildMemberFullName(string memberName, string containerId, string? nameQualifier = null)
    {
        if (nameQualifier is not null)
            return $"{nameQualifier}::{memberName}";

        return TryGetDeclaringTypeInfo(containerId, out var declaringTypeFullName, out _)
            ? $"{declaringTypeFullName}::{memberName}"
            : memberName;
    }

    private static Dictionary<string, string>? BuildMemberProperties(string containerId, IEnumerable<ParameterSyntax>? parameters = null)
    {
        Dictionary<string, string>? properties = null;

        if (TryGetDeclaringTypeInfo(containerId, out _, out var declaringTypeShortName))
        {
            properties = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["declaringTypeId"] = containerId,
                ["declaringTypeShortName"] = declaringTypeShortName
            };
        }

        var parameterArray = parameters?.ToArray();
        if (parameterArray is null)
            return properties;

        properties ??= new Dictionary<string, string>(StringComparer.Ordinal);
        properties["totalParameterCount"] = parameterArray.Length.ToString();
        properties["requiredParameterCount"] = CountRequiredParameters(parameterArray).ToString();
        return properties;
    }

    private static int CountRequiredParameters(IEnumerable<ParameterSyntax> parameters) =>
        parameters.Count(parameter =>
            parameter.Default is null
            && !parameter.Modifiers.Any(SyntaxKind.ParamsKeyword));

    private static bool TryGetDeclaringTypeInfo(string containerId, out string declaringTypeFullName, out string declaringTypeShortName)
    {
        foreach (var marker in new[] { "::Class::", "::Interface::", "::Struct::", "::Enum::", "::Delegate::" })
        {
            var markerIndex = containerId.IndexOf(marker, StringComparison.Ordinal);
            if (markerIndex < 0)
                continue;

            declaringTypeFullName = containerId[(markerIndex + marker.Length)..];
            var separatorIndex = declaringTypeFullName.LastIndexOf('.');
            declaringTypeShortName = separatorIndex >= 0
                ? declaringTypeFullName[(separatorIndex + 1)..]
                : declaringTypeFullName;
            return true;
        }

        declaringTypeFullName = string.Empty;
        declaringTypeShortName = string.Empty;
        return false;
    }

    private static Dictionary<string, string> BuildTypeMemberTypeMap(SeparatedSyntaxList<ParameterSyntax>? parameters)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var parameter in parameters ?? default)
            RememberType(map, parameter.Identifier.Text, parameter.Type?.ToString());

        return map;
    }

    private void PushIdentifierScope(SeparatedSyntaxList<ParameterSyntax> parameters)
    {
        var scope = _identifierTypeScopes.Count > 0
            ? new Dictionary<string, string>(_identifierTypeScopes.Peek(), StringComparer.Ordinal)
            : new Dictionary<string, string>(_currentTypeMemberTypes, StringComparer.Ordinal);

        foreach (var parameter in parameters)
            RememberType(scope, parameter.Identifier.Text, parameter.Type?.ToString());

        _identifierTypeScopes.Push(scope);
    }

    private void PopIdentifierScope()
    {
        if (_identifierTypeScopes.Count > 0)
            _identifierTypeScopes.Pop();
    }

    private static void RememberType(Dictionary<string, string> target, string identifier, string? rawType)
    {
        var typeName = CleanTypeName(rawType);
        if (typeName is null)
            return;

        target[identifier] = typeName;
    }

    private static string? ResolveVariableType(TypeSyntax declarationType, ExpressionSyntax? initializer)
    {
        if (!declarationType.IsVar)
            return declarationType.ToString();

        return initializer switch
        {
            ObjectCreationExpressionSyntax objectCreation => objectCreation.Type.ToString(),
            CastExpressionSyntax cast => cast.Type.ToString(),
            _ => null
        };
    }

    private static string InferTargetType(string typeName) =>
        typeName.StartsWith('I') ? "Interface" : "Class";

    private void AddInvocationEdges(SyntaxNode node, string sourceId)
    {
        var invocations = node.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(invocation => ReferenceEquals(FindOwningCallable(invocation), node))
            .Select(invocation => new
            {
                Name = ExtractCalleeName(invocation),
                ParamCount = invocation.ArgumentList.Arguments.Count,
                ReceiverTypeHint = ResolveReceiverTypeHint(invocation),
                ReceiverKind = ClassifyReceiver(invocation)
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
                ParamCount: callee.ParamCount,
                Properties: BuildCallProperties(callee.ReceiverTypeHint, callee.ReceiverKind)));
        }
    }

    private static SyntaxNode? FindOwningCallable(InvocationExpressionSyntax invocation) =>
        invocation.Ancestors().FirstOrDefault(ancestor => ancestor is
            MethodDeclarationSyntax or
            ConstructorDeclarationSyntax or
            LocalFunctionStatementSyntax or
            IndexerDeclarationSyntax or
            OperatorDeclarationSyntax or
            ConversionOperatorDeclarationSyntax or
            PropertyDeclarationSyntax or
            EventDeclarationSyntax or
            FieldDeclarationSyntax);

    private static Dictionary<string, string> BuildCallProperties(string? receiverTypeHint, string receiverKind)
    {
        var properties = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["receiverKind"] = receiverKind
        };
        if (receiverTypeHint is not null)
            properties["receiverTypeHint"] = receiverTypeHint;

        return properties;
    }

    private string ClassifyReceiver(InvocationExpressionSyntax invocation)
    {
        var receiver = GetReceiverExpression(invocation);
        if (receiver is null)
            return "Unqualified";

        if (receiver is ThisExpressionSyntax or BaseExpressionSyntax)
            return "ThisOrBase";

        return ResolveReceiverTypeHint(invocation) is not null
            ? "TypedOrStatic"
            : "UnknownMember";
    }

    private string? ResolveReceiverTypeHint(InvocationExpressionSyntax invocation)
    {
        var receiver = GetReceiverExpression(invocation);
        if (receiver is null)
            return null;

        return receiver switch
        {
            ThisExpressionSyntax => TryGetDeclaringTypeInfo(_currentTypeId ?? string.Empty, out _, out var currentTypeShortName)
                ? currentTypeShortName
                : null,
            BaseExpressionSyntax => null,
            IdentifierNameSyntax identifierName when _identifierTypeScopes.Count > 0
                && _identifierTypeScopes.Peek().TryGetValue(identifierName.Identifier.Text, out var scopedType) => scopedType,
            IdentifierNameSyntax identifierName when _currentTypeMemberTypes.TryGetValue(identifierName.Identifier.Text, out var memberType) => memberType,
            IdentifierNameSyntax identifierName when char.IsUpper(identifierName.Identifier.Text.FirstOrDefault()) => identifierName.Identifier.Text,
            ObjectCreationExpressionSyntax objectCreation => CleanTypeName(objectCreation.Type.ToString()),
            _ => null
        };
    }

    private static ExpressionSyntax? GetReceiverExpression(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
            return memberAccess.Expression;

        if (invocation.Expression is not MemberBindingExpressionSyntax)
            return null;

        return invocation.Ancestors()
            .OfType<ConditionalAccessExpressionSyntax>()
            .FirstOrDefault(conditional => conditional.WhenNotNull.Span.Contains(invocation.Span))
            ?.Expression;
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

        var targetType = InferTargetType(typeName);
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

        var start = trivia.IndexOf("<summary>", StringComparison.Ordinal);
        var end = trivia.IndexOf("</summary>", StringComparison.Ordinal);
        if (start < 0 || end < 0) return null;

        var raw = trivia[(start + 9)..end];
        return string.Join(" ", raw
            .Split('\n')
            .Select(l => l.TrimStart().TrimStart('/', ' ').Trim())
            .Where(l => l.Length > 0));
    }

    private static string? ExtractCalleeName(InvocationExpressionSyntax invocation) =>
        invocation.Expression switch
        {
            MemberAccessExpressionSyntax m => m.Name.Identifier.Text,
            MemberBindingExpressionSyntax m => m.Name.Identifier.Text,
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
