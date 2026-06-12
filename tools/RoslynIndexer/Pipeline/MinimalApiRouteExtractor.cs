using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMeridian.RoslynIndexer.Pipeline;

internal static class MinimalApiRouteExtractor
{
    public static void Extract(
        CompilationUnitSyntax root,
        string relPath,
        string projectContext,
        List<IngestNodeRequest> nodes,
        List<IngestEdgeRequest> edges,
        IReadOnlyDictionary<string, string> constants)
    {
        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (!TryGetInvokedName(invocation, out var invokedName))
                continue;

            var methods = RouteConstantResolver.ResolveHttpMethods(invokedName, invocation, constants);
            if (methods.Count == 0)
                continue;

            var routeTemplate = RouteConstantResolver.ResolveMinimalApiTemplate(invocation, constants);
            if (routeTemplate is null)
                continue;

            var prefix = ResolveMapGroupPrefix((SyntaxNode)invocation.Expression, constants);
            var fullRoute = CombineRoutes(prefix, routeTemplate);
            var lineNumber = invocation.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            var sourceId = RouteSourceResolver.ResolveMinimalApiSourceId(invocation, relPath, nodes, projectContext);
            var confidence = RouteTemplateNormalizer.Normalize(fullRoute).Contains("{param}", StringComparison.Ordinal) ? 0.9 : 1.0;

            foreach (var method in methods)
                AddEndpoint(nodes, edges, projectContext, sourceId, method, fullRoute, "aspnet-minimal-api", confidence, relPath, lineNumber);
        }
    }

    private static void AddEndpoint(
        List<IngestNodeRequest> nodes,
        List<IngestEdgeRequest> edges,
        string projectContext,
        string sourceId,
        string method,
        string template,
        string source,
        double confidence,
        string relPath,
        int lineNumber)
    {
        var normalizedRoute = RouteTemplateNormalizer.Normalize(template);
        var endpointId = $"{projectContext}::ApiEndpoint::{method} {normalizedRoute}";
        if (!nodes.Any(node => node.Id == endpointId))
        {
            nodes.Add(new IngestNodeRequest(
                endpointId,
                $"{method} {normalizedRoute}",
                "ApiEndpoint",
                Namespace: null,
                FilePath: null,
                LineNumber: null,
                Summary: $"Route endpoint ({source}) for `{method} {template}`",
                LineCount: null,
                SourceSnippet: null,
                SourceHash: null));
        }

        edges.Add(new IngestEdgeRequest(
            sourceId,
            endpointId,
            "Uses",
            CallSite: $"{relPath}:{lineNumber}",
            Confidence: confidence));
    }

    private static string CombineRoutes(string? prefix, string? route)
    {
        var left = prefix?.Trim() ?? string.Empty;
        var right = route?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(left))
            return string.IsNullOrWhiteSpace(right) ? "/" : right;

        if (string.IsNullOrWhiteSpace(right))
            return left;

        return $"{left.TrimEnd('/')}/{right.TrimStart('/')}";
    }

    private static string? ResolveMapGroupPrefix(
        SyntaxNode node,
        IReadOnlyDictionary<string, string> constants)
    {
        return node switch
        {
            InvocationExpressionSyntax invocation when TryGetInvokedName(invocation, out var invokedName) && invokedName == "MapGroup"
                => CombineRoutes(
                    invocation.Expression is MemberAccessExpressionSyntax groupMember
                        ? ResolveMapGroupPrefix(groupMember.Expression, constants)
                        : null,
                    RouteConstantResolver.ResolveMinimalApiTemplate(invocation, constants)),
            MemberAccessExpressionSyntax memberAccess => ResolveMapGroupPrefix(memberAccess.Expression, constants),
            _ => null
        };
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
