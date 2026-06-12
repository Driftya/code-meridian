using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMeridian.RoslynIndexer.Pipeline;

internal static class RouteConstantResolver
{
    public static IReadOnlyDictionary<string, string> BuildStringConstants(CompilationUnitSyntax root)
    {
        var constants = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var field in root.DescendantNodes().OfType<FieldDeclarationSyntax>())
        {
            if (!field.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.ConstKeyword)))
                continue;

            foreach (var variable in field.Declaration.Variables)
            {
                if (variable.Initializer?.Value is { } value && ResolveStringExpression(value, constants) is { } resolved)
                    constants[variable.Identifier.Text] = resolved;
            }
        }

        foreach (var local in root.DescendantNodes().OfType<LocalDeclarationStatementSyntax>())
        {
            if (!local.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.ConstKeyword)))
                continue;

            foreach (var variable in local.Declaration.Variables)
            {
                if (variable.Initializer?.Value is { } value && ResolveStringExpression(value, constants) is { } resolved)
                    constants[variable.Identifier.Text] = resolved;
            }
        }

        return constants;
    }

    public static string? ResolveAttributeString(AttributeSyntax attribute, IReadOnlyDictionary<string, string> constants)
    {
        if (attribute.ArgumentList is null || attribute.ArgumentList.Arguments.Count == 0)
            return null;

        return ResolveStringExpression(attribute.ArgumentList.Arguments[0].Expression, constants);
    }

    public static string? ResolveMinimalApiTemplate(
        InvocationExpressionSyntax invocation,
        IReadOnlyDictionary<string, string> constants)
    {
        if (invocation.ArgumentList.Arguments.Count == 0)
            return null;

        return ResolveStringExpression(invocation.ArgumentList.Arguments[0].Expression, constants);
    }

    public static List<string> ResolveHttpMethods(
        string invokedName,
        InvocationExpressionSyntax invocation,
        IReadOnlyDictionary<string, string> constants)
    {
        return invokedName switch
        {
            "MapGet" => ["GET"],
            "MapPost" => ["POST"],
            "MapPut" => ["PUT"],
            "MapPatch" => ["PATCH"],
            "MapDelete" => ["DELETE"],
            "MapMethods" => ResolveMapMethods(invocation, constants),
            _ => []
        };
    }

    public static string? ResolveStringExpression(ExpressionSyntax expression, IReadOnlyDictionary<string, string> constants)
    {
        return expression switch
        {
            LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.StringLiteralExpression)
                => literal.Token.ValueText,
            InterpolatedStringExpressionSyntax interpolated => string.Concat(interpolated.Contents.Select(content => content switch
            {
                InterpolatedStringTextSyntax text => text.TextToken.ValueText,
                InterpolationSyntax => "{param}",
                _ => string.Empty
            })),
            IdentifierNameSyntax identifier when constants.TryGetValue(identifier.Identifier.Text, out var value)
                => value,
            _ => null
        };
    }

    private static List<string> ResolveMapMethods(
        InvocationExpressionSyntax invocation,
        IReadOnlyDictionary<string, string> constants)
    {
        if (invocation.ArgumentList.Arguments.Count < 2)
            return [];

        var methodsArg = invocation.ArgumentList.Arguments[1].Expression;
        return methodsArg switch
        {
            ArrayCreationExpressionSyntax arrayCreation => arrayCreation.Initializer?.Expressions
                .Select(expr => ResolveStringExpression(expr, constants)?.ToUpperInvariant())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Cast<string>()
                .Distinct(StringComparer.Ordinal)
                .ToList() ?? [],
            ImplicitArrayCreationExpressionSyntax implicitArray => implicitArray.Initializer.Expressions
                .Select(expr => ResolveStringExpression(expr, constants)?.ToUpperInvariant())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Cast<string>()
                .Distinct(StringComparer.Ordinal)
                .ToList(),
            CollectionExpressionSyntax collectionExpression => collectionExpression.Elements
                .OfType<ExpressionElementSyntax>()
                .Select(element => ResolveStringExpression(element.Expression, constants)?.ToUpperInvariant())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Cast<string>()
                .Distinct(StringComparer.Ordinal)
                .ToList(),
            _ => []
        };
    }
}
