using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMeridian.RoslynIndexer.Pipeline;

internal sealed record CSharpInvocationEvidence(
    string Name,
    int ParameterCount,
    string ReceiverKind,
    string? ReceiverTypeHint,
    string? ReceiverCanonicalTypeHint,
    string? DeclaringTypeHint,
    string? TargetDeclaringTypeHint,
    int GenericArity,
    string EvidenceSource,
    string EvidenceConfidence);

internal static class CSharpInvocationEvidenceExtractor
{
    private static readonly IReadOnlySet<string> KnownFrameworkTypeNames = new HashSet<string>(
        [
            "Activator", "ArgumentException", "ArgumentNullException", "Array", "Console", "Convert",
            "DateTime", "DateTimeOffset", "Enum", "Environment", "Guid", "Math", "TimeSpan",
            "Task", "ValueTask", "Enumerable", "StringComparer"
        ],
        StringComparer.Ordinal);

    public static CSharpInvocationEvidence? Extract(
        InvocationExpressionSyntax invocation,
        SyntaxNode owningCallable,
        string? currentTypeShortName,
        IReadOnlyDictionary<string, string> scopedTypes,
        IReadOnlyDictionary<string, string> memberTypes,
        IReadOnlySet<string> knownTypeNames,
        IReadOnlyDictionary<string, string> typeAliases,
        SemanticModel? semanticModel = null)
    {
        if (!ReferenceEquals(FindOwningCallable(invocation), owningCallable))
            return null;

        var name = ExtractCalleeName(invocation);
        if (name is null)
            return null;

        var receiver = GetReceiverExpression(invocation);
        var receiverEvidence = receiver is null
            ? new ReceiverEvidence("Unqualified", null, "syntax-unqualified", "Exact")
            : ResolveReceiverEvidence(
                receiver,
                invocation,
                currentTypeShortName,
                scopedTypes,
                memberTypes,
                knownTypeNames,
                typeAliases);
        var semanticEvidence = semanticModel is null
            ? null
            : ResolveSemanticEvidence(invocation, receiver, semanticModel);
        if (semanticEvidence?.ReceiverType is not null)
        {
            receiverEvidence = new ReceiverEvidence(
                "TypedOrStatic",
                semanticEvidence.ReceiverType,
                semanticEvidence.IsStaticReceiver ? "semantic-model-static" : "semantic-model-instance",
                "Exact");
        }

        return new CSharpInvocationEvidence(
            name,
            invocation.ArgumentList.Arguments.Count,
            receiverEvidence.Kind,
            receiverEvidence.Type?.ShortName,
            receiverEvidence.Type?.CanonicalName,
            currentTypeShortName,
            semanticEvidence?.TargetDeclaringType?.CanonicalName,
            GetGenericArity(invocation),
            receiverEvidence.Source,
            receiverEvidence.Confidence);
    }

    private static SemanticInvocationEvidence? ResolveSemanticEvidence(
        InvocationExpressionSyntax invocation,
        ExpressionSyntax? receiver,
        SemanticModel semanticModel)
    {
        var targetMethod = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        var targetDeclaringType = NormalizeSemanticType(targetMethod?.ContainingType);
        if (receiver is null)
        {
            return targetDeclaringType is null
                ? null
                : new SemanticInvocationEvidence(null, targetDeclaringType, false);
        }

        var receiverSymbol = semanticModel.GetSymbolInfo(receiver).Symbol;
        var receiverType = NormalizeSemanticType(
            semanticModel.GetTypeInfo(receiver).Type
            ?? receiverSymbol as ITypeSymbol);
        if (receiverType is null && targetDeclaringType is null)
            return null;

        return new SemanticInvocationEvidence(
            receiverType,
            targetDeclaringType,
            receiverSymbol is ITypeSymbol);
    }

    private static CSharpTypeIdentity? NormalizeSemanticType(ITypeSymbol? type)
    {
        if (type is null or IErrorTypeSymbol || type.TypeKind == TypeKind.Dynamic)
            return null;

        var displayName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        const string globalPrefix = "global::";
        if (displayName.StartsWith(globalPrefix, StringComparison.Ordinal))
            displayName = displayName[globalPrefix.Length..];

        return CSharpTypeIdentityNormalizer.Normalize(displayName);
    }

    private static ReceiverEvidence ResolveReceiverEvidence(
        ExpressionSyntax receiver,
        InvocationExpressionSyntax invocation,
        string? currentTypeShortName,
        IReadOnlyDictionary<string, string> scopedTypes,
        IReadOnlyDictionary<string, string> memberTypes,
        IReadOnlySet<string> knownTypeNames,
        IReadOnlyDictionary<string, string> typeAliases)
    {
        switch (receiver)
        {
            case ThisExpressionSyntax:
                return Exact(currentTypeShortName, "syntax-this");
            case BaseExpressionSyntax:
                return Exact(currentTypeShortName, "syntax-base", "ThisOrBase");
            case PredefinedTypeSyntax predefinedType:
                return Exact(predefinedType.Keyword.Text, "syntax-predefined");
            case IdentifierNameSyntax identifier:
                return ResolveIdentifierEvidence(
                    identifier.Identifier.Text,
                    invocation,
                    scopedTypes,
                    memberTypes,
                    knownTypeNames,
                    typeAliases);
            case ObjectCreationExpressionSyntax objectCreation:
                return Exact(objectCreation.Type.ToString(), "syntax-object-creation");
            case CastExpressionSyntax cast:
                return Exact(cast.Type.ToString(), "syntax-cast");
            case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.AsExpression):
                return Exact(binary.Right.ToString(), "syntax-as-cast");
            case MemberAccessExpressionSyntax memberAccess
                when memberAccess.Expression is ThisExpressionSyntax
                && memberTypes.TryGetValue(memberAccess.Name.Identifier.Text, out var memberType):
                return Exact(memberType, "syntax-this-member");
            case ParenthesizedExpressionSyntax parenthesized:
                return ResolveReceiverEvidence(
                    parenthesized.Expression,
                    invocation,
                    currentTypeShortName,
                    scopedTypes,
                    memberTypes,
                    knownTypeNames,
                    typeAliases);
            case PostfixUnaryExpressionSyntax postfix when postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression):
                return ResolveReceiverEvidence(
                    postfix.Operand,
                    invocation,
                    currentTypeShortName,
                    scopedTypes,
                    memberTypes,
                    knownTypeNames,
                    typeAliases);
            case ConditionalExpressionSyntax conditional:
            {
                var whenTrue = ResolveReceiverEvidence(
                    conditional.WhenTrue,
                    invocation,
                    currentTypeShortName,
                    scopedTypes,
                    memberTypes,
                    knownTypeNames,
                    typeAliases);
                var whenFalse = ResolveReceiverEvidence(
                    conditional.WhenFalse,
                    invocation,
                    currentTypeShortName,
                    scopedTypes,
                    memberTypes,
                    knownTypeNames,
                    typeAliases);
                if (whenTrue.Type is not null
                    && string.Equals(whenTrue.Type.CanonicalName, whenFalse.Type?.CanonicalName, StringComparison.Ordinal))
                {
                    return whenTrue with { Source = "syntax-conditional", Confidence = "Exact" };
                }
                break;
            }
            case MemberAccessExpressionSyntax qualified
                when TryResolveQualifiedStaticType(qualified, knownTypeNames, out var qualifiedType):
                return new ReceiverEvidence("TypedOrStatic", qualifiedType, "syntax-qualified-type", "Exact");
        }

        var chainRoot = ResolveChainRoot(
            receiver,
            invocation,
            currentTypeShortName,
            scopedTypes,
            memberTypes,
            knownTypeNames,
            typeAliases);
        return chainRoot.Type is null
            ? new ReceiverEvidence("UnknownMember", null, "syntax-unknown", "Unknown")
            : new ReceiverEvidence("Chained", chainRoot.Type, "syntax-chain-root", "RootOnly");
    }

    private static ReceiverEvidence ResolveIdentifierEvidence(
        string identifier,
        InvocationExpressionSyntax invocation,
        IReadOnlyDictionary<string, string> scopedTypes,
        IReadOnlyDictionary<string, string> memberTypes,
        IReadOnlySet<string> knownTypeNames,
        IReadOnlyDictionary<string, string> typeAliases)
    {
        if (CSharpLexicalTypeResolver.Resolve(invocation, identifier) is { } lexicalType)
            return Exact(lexicalType, "syntax-lexical-variable");
        if (scopedTypes.TryGetValue(identifier, out var scopedType))
            return Exact(scopedType, "syntax-parameter");
        if (memberTypes.TryGetValue(identifier, out var memberType))
            return Exact(memberType, "syntax-member");
        if (typeAliases.TryGetValue(identifier, out var aliasedType))
            return Exact(aliasedType, "syntax-using-alias");
        if (knownTypeNames.Contains(identifier))
            return Exact(identifier, "syntax-local-type-catalog");
        if (KnownFrameworkTypeNames.Contains(identifier))
            return Exact(identifier, "syntax-framework-type-catalog");

        return new ReceiverEvidence("UnknownMember", null, "syntax-unbound-identifier", "Unknown");
    }

    private static ReceiverEvidence ResolveChainRoot(
        ExpressionSyntax receiver,
        InvocationExpressionSyntax invocation,
        string? currentTypeShortName,
        IReadOnlyDictionary<string, string> scopedTypes,
        IReadOnlyDictionary<string, string> memberTypes,
        IReadOnlySet<string> knownTypeNames,
        IReadOnlyDictionary<string, string> typeAliases)
    {
        ExpressionSyntax? root = receiver switch
        {
            InvocationExpressionSyntax nestedInvocation => GetReceiverExpression(nestedInvocation),
            MemberAccessExpressionSyntax memberAccess => memberAccess.Expression,
            ConditionalAccessExpressionSyntax conditionalAccess => conditionalAccess.Expression,
            ElementAccessExpressionSyntax elementAccess => elementAccess.Expression,
            AwaitExpressionSyntax awaitExpression => awaitExpression.Expression,
            _ => null
        };
        if (root is null || ReferenceEquals(root, receiver))
            return new ReceiverEvidence("UnknownMember", null, "syntax-unknown", "Unknown");

        return ResolveReceiverEvidence(
            root,
            invocation,
            currentTypeShortName,
            scopedTypes,
            memberTypes,
            knownTypeNames,
            typeAliases);
    }

    private static SyntaxNode? FindOwningCallable(SyntaxNode node) =>
        node.Ancestors().FirstOrDefault(ancestor => ancestor is
            MethodDeclarationSyntax or
            ConstructorDeclarationSyntax or
            LocalFunctionStatementSyntax or
            IndexerDeclarationSyntax or
            OperatorDeclarationSyntax or
            ConversionOperatorDeclarationSyntax or
            PropertyDeclarationSyntax or
            EventDeclarationSyntax or
            FieldDeclarationSyntax);

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

    private static bool TryResolveQualifiedStaticType(
        MemberAccessExpressionSyntax expression,
        IReadOnlySet<string> knownTypeNames,
        out CSharpTypeIdentity? type)
    {
        type = null;
        var text = expression.ToString();
        if (!text.StartsWith("System.", StringComparison.Ordinal)
            && !knownTypeNames.Contains(expression.Name.Identifier.Text))
        {
            return false;
        }

        type = CSharpTypeIdentityNormalizer.Normalize(text);
        return type is not null;
    }

    private static int GetGenericArity(InvocationExpressionSyntax invocation) =>
        invocation.Expression switch
        {
            GenericNameSyntax genericName => genericName.TypeArgumentList.Arguments.Count,
            MemberAccessExpressionSyntax { Name: GenericNameSyntax genericName } =>
                genericName.TypeArgumentList.Arguments.Count,
            MemberBindingExpressionSyntax { Name: GenericNameSyntax genericName } =>
                genericName.TypeArgumentList.Arguments.Count,
            _ => 0
        };

    private static string? ExtractCalleeName(InvocationExpressionSyntax invocation) =>
        invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.Text,
            MemberBindingExpressionSyntax memberBinding => memberBinding.Name.Identifier.Text,
            IdentifierNameSyntax identifier => identifier.Identifier.Text,
            GenericNameSyntax genericName => genericName.Identifier.Text,
            _ => null
        };

    private static ReceiverEvidence Exact(
        string? rawType,
        string source,
        string kind = "TypedOrStatic") =>
        new(kind, CSharpTypeIdentityNormalizer.Normalize(rawType), source, "Exact");

    private sealed record ReceiverEvidence(
        string Kind,
        CSharpTypeIdentity? Type,
        string Source,
        string Confidence);

    private sealed record SemanticInvocationEvidence(
        CSharpTypeIdentity? ReceiverType,
        CSharpTypeIdentity? TargetDeclaringType,
        bool IsStaticReceiver);
}
