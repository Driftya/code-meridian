using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMeridian.RoslynIndexer.Pipeline;

internal static class CSharpLexicalTypeResolver
{
    public static string? Resolve(
        InvocationExpressionSyntax invocation,
        string identifier) =>
        ResolveLocalDeclaration(invocation, identifier)
        ?? ResolveContextualDeclaration(invocation, identifier);

    private static string? ResolveLocalDeclaration(
        InvocationExpressionSyntax invocation,
        string identifier)
    {
        var owningCallable = FindOwningCallable(invocation);
        if (owningCallable is null)
            return null;

        var blocks = invocation.Ancestors().OfType<BlockSyntax>().ToHashSet();
        var variable = owningCallable.DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .Where(candidate => candidate.Identifier.Text == identifier && candidate.SpanStart < invocation.SpanStart)
            .Where(candidate => ReferenceEquals(FindOwningCallable(candidate), owningCallable))
            .Where(candidate => IsVisibleAtInvocation(candidate, invocation, blocks))
            .OrderByDescending(candidate => candidate.SpanStart)
            .FirstOrDefault();
        if (variable?.Parent is VariableDeclarationSyntax declaration)
            return ResolveVariableType(declaration.Type, variable.Initializer?.Value);

        var declarationExpression = owningCallable.DescendantNodes()
            .OfType<DeclarationExpressionSyntax>()
            .Where(candidate => candidate.SpanStart < invocation.SpanStart)
            .Where(candidate => candidate.Designation.DescendantNodesAndSelf()
                .OfType<SingleVariableDesignationSyntax>()
                .Any(designation => designation.Identifier.Text == identifier))
            .Where(candidate => ReferenceEquals(FindOwningCallable(candidate), owningCallable))
            .Where(candidate => IsVisibleAtInvocation(candidate, invocation, blocks))
            .OrderByDescending(candidate => candidate.SpanStart)
            .FirstOrDefault();
        return declarationExpression?.Type.IsVar == false
            ? declarationExpression.Type.ToString()
            : null;
    }

    private static string? ResolveContextualDeclaration(
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
                return parameter.Type.ToString();

            if (ancestor is ForEachStatementSyntax forEach
                && forEach.Identifier.Text == identifier
                && !forEach.Type.IsVar)
            {
                return forEach.Type.ToString();
            }

            if (ancestor is CatchClauseSyntax { Declaration: { } declaration }
                && declaration.Identifier.Text == identifier)
            {
                return declaration.Type.ToString();
            }

            var declarationPattern = ancestor.DescendantNodesAndSelf()
                .OfType<DeclarationPatternSyntax>()
                .FirstOrDefault(pattern => pattern.Designation is SingleVariableDesignationSyntax designation
                    && designation.Identifier.Text == identifier);
            if (declarationPattern is not null)
                return declarationPattern.Type.ToString();

            if (ancestor is MethodDeclarationSyntax or ConstructorDeclarationSyntax or LocalFunctionStatementSyntax)
                break;
        }

        return null;
    }

    private static bool IsVisibleAtInvocation(
        SyntaxNode declaration,
        InvocationExpressionSyntax invocation,
        IReadOnlySet<BlockSyntax> invocationBlocks)
    {
        var block = declaration.Ancestors().OfType<BlockSyntax>().FirstOrDefault();
        if (block is not null && invocationBlocks.Contains(block))
            return true;

        var usingStatement = declaration.Ancestors().OfType<UsingStatementSyntax>().FirstOrDefault();
        if (usingStatement?.Statement.Span.Contains(invocation.Span) == true)
            return true;

        var forStatement = declaration.Ancestors().OfType<ForStatementSyntax>().FirstOrDefault();
        return forStatement?.Statement.Span.Contains(invocation.Span) == true;
    }

    private static string? ResolveVariableType(TypeSyntax declarationType, ExpressionSyntax? initializer)
    {
        if (!declarationType.IsVar)
            return declarationType.ToString();

        initializer = Unwrap(initializer);
        return initializer switch
        {
            ObjectCreationExpressionSyntax objectCreation => objectCreation.Type.ToString(),
            CastExpressionSyntax cast => cast.Type.ToString(),
            BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.AsExpression) => binary.Right.ToString(),
            _ => null
        };
    }

    private static ExpressionSyntax? Unwrap(ExpressionSyntax? expression) =>
        expression switch
        {
            ParenthesizedExpressionSyntax parenthesized => Unwrap(parenthesized.Expression),
            PostfixUnaryExpressionSyntax postfix when postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression) =>
                Unwrap(postfix.Operand),
            _ => expression
        };

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
}
