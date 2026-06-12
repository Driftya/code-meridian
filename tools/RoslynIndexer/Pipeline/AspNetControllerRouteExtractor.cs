using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMeridian.RoslynIndexer.Pipeline;

internal static class AspNetControllerRouteExtractor
{
    public static void Extract(
        CompilationUnitSyntax root,
        string relPath,
        string projectContext,
        List<IngestNodeRequest> nodes,
        List<IngestEdgeRequest> edges,
        IReadOnlyDictionary<string, string> constants)
    {
        foreach (var classNode in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
        {
            var controllerPrefix = ResolveControllerPrefix(classNode, constants);
            foreach (var methodNode in classNode.Members.OfType<MethodDeclarationSyntax>())
            {
                var methodId = RouteSourceResolver.ResolveControllerMethodId(nodes, relPath, methodNode);
                if (methodId is null)
                    continue;

                var actionRoutes = ResolveControllerActionRoutes(classNode, methodNode, constants);
                foreach (var route in actionRoutes)
                    AddEndpoint(nodes, edges, projectContext, methodId, route.Method, route.Template, route.Source, route.Confidence, relPath, methodNode.GetLocation().GetLineSpan().StartLinePosition.Line + 1);

                if (actionRoutes.Count == 0 && controllerPrefix is not null)
                {
                    foreach (var attribute in methodNode.AttributeLists.SelectMany(list => list.Attributes))
                    {
                        if (!IsRouteAttribute(attribute))
                            continue;

                        var template = RouteConstantResolver.ResolveAttributeString(attribute, constants);
                        if (template is null)
                            continue;

                        AddEndpoint(nodes, edges, projectContext, methodId, "ANY", CombineRoutes(controllerPrefix, template), "aspnet-controller", 0.85, relPath, methodNode.GetLocation().GetLineSpan().StartLinePosition.Line + 1);
                    }
                }
            }
        }
    }

    private static List<RouteCandidate> ResolveControllerActionRoutes(
        ClassDeclarationSyntax classNode,
        MethodDeclarationSyntax methodNode,
        IReadOnlyDictionary<string, string> constants)
    {
        var classRoute = ResolveControllerPrefix(classNode, constants);
        var results = new List<RouteCandidate>();

        foreach (var attribute in methodNode.AttributeLists.SelectMany(list => list.Attributes))
        {
            var attributeName = AttributeName(attribute);
            if (attributeName is null)
                continue;

            var method = attributeName switch
            {
                "HttpGet" => "GET",
                "HttpPost" => "POST",
                "HttpPut" => "PUT",
                "HttpPatch" => "PATCH",
                "HttpDelete" => "DELETE",
                _ => null
            };

            if (method is null)
                continue;

            var template = RouteConstantResolver.ResolveAttributeString(attribute, constants);
            var composed = CombineRoutes(classRoute, template);
            results.Add(new RouteCandidate(method, ReplaceControllerTokens(composed, classNode, methodNode), "aspnet-controller", template is null ? 0.9 : 1.0));
        }

        return results;
    }

    private static string? ResolveControllerPrefix(
        ClassDeclarationSyntax classNode,
        IReadOnlyDictionary<string, string> constants)
    {
        foreach (var attribute in classNode.AttributeLists.SelectMany(list => list.Attributes))
        {
            if (!IsRouteAttribute(attribute))
                continue;

            var template = RouteConstantResolver.ResolveAttributeString(attribute, constants);
            if (template is null)
                continue;

            return ReplaceControllerTokens(template, classNode, methodNode: null);
        }

        return null;
    }

    private static string ReplaceControllerTokens(
        string route,
        ClassDeclarationSyntax classNode,
        MethodDeclarationSyntax? methodNode)
    {
        var controllerName = classNode.Identifier.Text.EndsWith("Controller", StringComparison.Ordinal)
            ? classNode.Identifier.Text[..^"Controller".Length]
            : classNode.Identifier.Text;

        return route
            .Replace("[controller]", controllerName, StringComparison.OrdinalIgnoreCase)
            .Replace("[action]", methodNode?.Identifier.Text ?? string.Empty, StringComparison.OrdinalIgnoreCase);
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

    private static bool IsRouteAttribute(AttributeSyntax attribute)
    {
        var name = AttributeName(attribute);
        return string.Equals(name, "Route", StringComparison.OrdinalIgnoreCase);
    }

    private static string? AttributeName(AttributeSyntax attribute)
    {
        var raw = attribute.Name.ToString();
        if (raw.Contains('.'))
            raw = raw.Split('.').Last();
        return raw.EndsWith("Attribute", StringComparison.Ordinal)
            ? raw[..^"Attribute".Length]
            : raw;
    }

    private sealed record RouteCandidate(string Method, string Template, string Source, double Confidence);
}
