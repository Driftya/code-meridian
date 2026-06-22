using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMeridian.RoslynIndexer.Pipeline;

internal static class CSharpIndexerSyntaxUtilities
{
    public static string ResolveSourceId(SyntaxNode syntaxNode, string relativePath, string projectContext)
    {
        if (syntaxNode.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault() is { } method)
            return BuildMethodId(method, projectContext);

        if (syntaxNode.Ancestors().OfType<ConstructorDeclarationSyntax>().FirstOrDefault() is { } ctor)
            return BuildConstructorId(ctor, projectContext);

        if (syntaxNode.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault() is { } classDeclaration)
            return BuildTypeId("Class", classDeclaration.Identifier.Text, classDeclaration, projectContext);

        return $"{projectContext}::File::{relativePath}";
    }

    public static int GetLineNumber(SyntaxNode syntaxNode) =>
        syntaxNode.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

    public static string? ResolveStringExpression(ExpressionSyntax? expression, CSharpConfigurationConstantRegistry constants)
    {
        switch (expression)
        {
            case LiteralExpressionSyntax literal when literal.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.StringLiteralExpression):
                return literal.Token.ValueText;
            case InterpolatedStringExpressionSyntax interpolated:
                var parts = new List<string>();
                foreach (var content in interpolated.Contents)
                {
                    if (content is InterpolatedStringTextSyntax text)
                    {
                        parts.Add(text.TextToken.ValueText);
                        continue;
                    }

                    if (content is InterpolationSyntax interpolation)
                    {
                        var resolvedPart = ResolveStringExpression(interpolation.Expression, constants);
                        if (resolvedPart is null)
                            return null;

                        parts.Add(resolvedPart);
                    }
                }

                return string.Concat(parts);
            case MemberAccessExpressionSyntax:
                return constants.TryResolve(expression, out var memberResolved) ? memberResolved : null;
            case IdentifierNameSyntax identifier:
                if (constants.TryResolve(identifier, out var identifierResolved))
                    return identifierResolved;

                return ResolveLocalStringDeclaration(identifier, constants);
            default:
                return null;
        }
    }

    private static string? ResolveLocalStringDeclaration(IdentifierNameSyntax identifier, CSharpConfigurationConstantRegistry constants)
    {
        var currentSpanStart = identifier.SpanStart;
        var declarator = identifier.Ancestors()
            .OfType<MemberDeclarationSyntax>()
            .FirstOrDefault()?
            .DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .Where(candidate =>
                candidate.Identifier.ValueText == identifier.Identifier.ValueText
                && candidate.SpanStart < currentSpanStart)
            .OrderByDescending(candidate => candidate.SpanStart)
            .FirstOrDefault();

        return declarator?.Initializer?.Value is { } initializer
            ? ResolveStringExpression(initializer, constants)
            : null;
    }

    private static string BuildMethodId(MethodDeclarationSyntax method, string projectContext)
    {
        var signature = BuildSignature(method.Identifier.Text, method.ParameterList.Parameters);
        return $"{projectContext}::Method::{BuildQualifiedMemberName(method, signature)}";
    }

    private static string BuildConstructorId(ConstructorDeclarationSyntax ctor, string projectContext)
    {
        var signature = BuildSignature(ctor.Identifier.Text, ctor.ParameterList.Parameters);
        return $"{projectContext}::Method::{BuildQualifiedMemberName(ctor, signature)}";
    }

    private static string BuildTypeId(string kind, string localName, SyntaxNode syntaxNode, string projectContext)
    {
        var namespaceName = syntaxNode.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault()?.Name.ToString();
        var fullName = string.IsNullOrWhiteSpace(namespaceName) ? localName : $"{namespaceName}.{localName}";
        return $"{projectContext}::{kind}::{fullName}";
    }

    private static string BuildQualifiedMemberName(SyntaxNode syntaxNode, string localName)
    {
        var namespaceName = syntaxNode.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault()?.Name.ToString();
        return string.IsNullOrWhiteSpace(namespaceName)
            ? localName
            : $"{namespaceName}.{localName}";
    }

    private static string BuildSignature(string methodName, SeparatedSyntaxList<ParameterSyntax> parameters) =>
        $"{methodName}({string.Join(",", parameters.Select(parameter => parameter.Type?.ToString() ?? "?"))})";
}
