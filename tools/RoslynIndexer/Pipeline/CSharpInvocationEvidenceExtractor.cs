using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMeridian.RoslynIndexer.Pipeline;

internal sealed record CSharpInvocationEvidence(
    string Name,
    int ParameterCount,
    string ReceiverKind,
    string? ReceiverTypeHint,
    int GenericArity);

internal static class CSharpInvocationEvidenceExtractor
{
    public static CSharpInvocationEvidence? Extract(
        InvocationExpressionSyntax invocation,
        SyntaxNode owningCallable,
        string? currentTypeShortName,
        IReadOnlyDictionary<string, string> scopedTypes,
        IReadOnlyDictionary<string, string> memberTypes)
    {
        if (!ReferenceEquals(FindOwningCallable(invocation), owningCallable))
            return null;

        var name = ExtractCalleeName(invocation);
        if (name is null)
            return null;

        var receiver = GetReceiverExpression(invocation);
        var receiverTypeHint = receiver is null
            ? null
            : ResolveReceiverExpressionType(
                receiver,
                invocation,
                currentTypeShortName,
                scopedTypes,
                memberTypes);
        var receiverKind = receiver switch
        {
            null => "Unqualified",
            ThisExpressionSyntax or BaseExpressionSyntax => "ThisOrBase",
            _ when receiverTypeHint is not null => "TypedOrStatic",
            _ => "UnknownMember"
        };

        return new CSharpInvocationEvidence(
            name,
            invocation.ArgumentList.Arguments.Count,
            receiverKind,
            receiverTypeHint,
            GetGenericArity(invocation));
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

    private static string? ResolveReceiverExpressionType(
        ExpressionSyntax receiver,
        InvocationExpressionSyntax invocation,
        string? currentTypeShortName,
        IReadOnlyDictionary<string, string> scopedTypes,
        IReadOnlyDictionary<string, string> memberTypes) =>
        receiver switch
        {
            ThisExpressionSyntax => currentTypeShortName,
            BaseExpressionSyntax => null,
            PredefinedTypeSyntax predefinedType => predefinedType.Keyword.Text,
            IdentifierNameSyntax identifier when scopedTypes.TryGetValue(identifier.Identifier.Text, out var scopedType) => scopedType,
            IdentifierNameSyntax identifier when memberTypes.TryGetValue(identifier.Identifier.Text, out var memberType) => memberType,
            IdentifierNameSyntax identifier when ResolveContextualIdentifierType(invocation, identifier.Identifier.Text) is { } contextualType => contextualType,
            IdentifierNameSyntax identifier when char.IsUpper(identifier.Identifier.Text.FirstOrDefault()) => identifier.Identifier.Text,
            ObjectCreationExpressionSyntax objectCreation => CleanTypeName(objectCreation.Type.ToString()),
            CastExpressionSyntax cast => CleanTypeName(cast.Type.ToString()),
            MemberAccessExpressionSyntax memberAccess
                when memberAccess.Expression is ThisExpressionSyntax
                && memberTypes.TryGetValue(memberAccess.Name.Identifier.Text, out var memberType) => memberType,
            ParenthesizedExpressionSyntax parenthesized => ResolveReceiverExpressionType(
                parenthesized.Expression,
                invocation,
                currentTypeShortName,
                scopedTypes,
                memberTypes),
            PostfixUnaryExpressionSyntax postfix when postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression) =>
                ResolveReceiverExpressionType(
                    postfix.Operand,
                    invocation,
                    currentTypeShortName,
                    scopedTypes,
                    memberTypes),
            _ => null
        };

    private static string? ResolveContextualIdentifierType(
        InvocationExpressionSyntax invocation,
        string identifier)
    {
        foreach (var ancestor in invocation.Ancestors())
        {
            var parameter = ancestor switch
            {
                SimpleLambdaExpressionSyntax simpleLambda
                    when simpleLambda.Parameter.Identifier.Text == identifier => simpleLambda.Parameter,
                ParenthesizedLambdaExpressionSyntax parenthesizedLambda => parenthesizedLambda.ParameterList.Parameters
                    .FirstOrDefault(candidate => candidate.Identifier.Text == identifier),
                AnonymousMethodExpressionSyntax anonymousMethod => anonymousMethod.ParameterList?.Parameters
                    .FirstOrDefault(candidate => candidate.Identifier.Text == identifier),
                _ => null
            };
            if (parameter?.Type is not null)
                return CleanTypeName(parameter.Type.ToString());

            if (ancestor is ForEachStatementSyntax forEach
                && forEach.Identifier.Text == identifier)
            {
                return CleanTypeName(forEach.Type.ToString());
            }

            if (ancestor is CatchClauseSyntax { Declaration: { } declaration }
                && declaration.Identifier.Text == identifier)
            {
                return CleanTypeName(declaration.Type.ToString());
            }

            if (ancestor is IfStatementSyntax ifStatement)
            {
                var declarationPattern = ifStatement.Condition.DescendantNodesAndSelf()
                    .OfType<DeclarationPatternSyntax>()
                    .FirstOrDefault(pattern => pattern.Designation is SingleVariableDesignationSyntax designation
                        && designation.Identifier.Text == identifier);
                if (declarationPattern is not null)
                    return CleanTypeName(declarationPattern.Type.ToString());
            }

            if (ancestor is MethodDeclarationSyntax or ConstructorDeclarationSyntax or LocalFunctionStatementSyntax)
                break;
        }

        return null;
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
}
