using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMeridian.RoslynIndexer.Pipeline;

internal static class RouteSourceResolver
{
    public static string ResolveMinimalApiSourceId(
        InvocationExpressionSyntax invocation,
        string relPath,
        IReadOnlyList<IngestNodeRequest> nodes,
        string projectContext)
    {
        var fileId = $"{projectContext}::File::{relPath}";
        var handlerArg = GetMinimalApiHandlerArgument(invocation);
        if (handlerArg is null)
            return fileId;

        var handlerExpression = handlerArg.Expression;
        var handlerName = handlerExpression switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.Text,
            MemberAccessExpressionSyntax member => member.Name.Identifier.Text,
            _ => null
        };

        if (handlerName is null)
            return TryResolveContainingMethodId(invocation, nodes, relPath) ?? fileId;

        var matches = nodes
            .Where(node => node.Type == "Method"
                && string.Equals(node.FilePath, relPath, StringComparison.OrdinalIgnoreCase)
                && ShortMethodName(node.Name) == handlerName)
            .Select(node => node.Id)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (matches.Length == 1)
            return matches[0];

        if (TryResolveContainingMethodId(invocation, nodes, relPath) is { } containingMethodId)
            return containingMethodId;

        return fileId;
    }

    public static string? ResolveControllerMethodId(
        IReadOnlyList<IngestNodeRequest> nodes,
        string relPath,
        MethodDeclarationSyntax methodNode)
    {
        var lineNumber = methodNode.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        var methodName = methodNode.Identifier.Text;
        var matches = nodes
            .Where(node => node.Type == "Method"
                && string.Equals(node.FilePath, relPath, StringComparison.OrdinalIgnoreCase)
                && node.LineNumber == lineNumber
                && ShortMethodName(node.Name) == methodName)
            .Select(node => node.Id)
            .ToArray();

        return matches.Length == 1 ? matches[0] : null;
    }

    public static string? TryResolveContainingMethodId(
        SyntaxNode node,
        IReadOnlyList<IngestNodeRequest> nodes,
        string relPath)
    {
        var containingMethod = node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        if (containingMethod is null)
            return null;

        return ResolveControllerMethodId(nodes, relPath, containingMethod);
    }

    private static ArgumentSyntax? GetMinimalApiHandlerArgument(InvocationExpressionSyntax invocation)
    {
        if (!TryGetInvokedName(invocation, out var invokedName))
            return null;

        var index = invokedName == "MapMethods" ? 2 : 1;
        return invocation.ArgumentList.Arguments.Count > index
            ? invocation.ArgumentList.Arguments[index]
            : null;
    }

    private static string ShortMethodName(string signature)
    {
        var withoutParams = signature.Split('(')[0];
        var lastDot = withoutParams.LastIndexOf('.');
        return lastDot >= 0 ? withoutParams[(lastDot + 1)..] : withoutParams;
    }

    private static bool TryGetInvokedName(InvocationExpressionSyntax invocation, out string invokedName)
    {
        invokedName = invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.Text,
            IdentifierNameSyntax identifier => identifier.Identifier.Text,
            _ => string.Empty
        };

        return invokedName.Length > 0;
    }
}
