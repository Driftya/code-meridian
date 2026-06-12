using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMeridian.RoslynIndexer.Pipeline;

internal static partial class CSharpRouteExtractor
{
    public static void Extract(
        CompilationUnitSyntax root,
        string relPath,
        string projectContext,
        List<IngestNodeRequest> nodes,
        List<IngestEdgeRequest> edges)
    {
        var constants = BuildStringConstants(root);
        ExtractControllerRoutes(root, relPath, projectContext, nodes, edges, constants);
        ExtractMinimalApiRoutes(root, relPath, projectContext, nodes, edges, constants);
    }

    private static void ExtractControllerRoutes(
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
                var methodId = TryFindMethodId(nodes, relPath, methodNode);
                if (methodId is null)
                    continue;

                var actionRoutes = ResolveControllerActionRoutes(classNode, methodNode, constants);
                foreach (var route in actionRoutes)
                {
                    AddEndpoint(nodes, edges, projectContext, methodId, route.Method, route.Template, route.Source, route.Confidence, relPath, methodNode.GetLocation().GetLineSpan().StartLinePosition.Line + 1);
                }

                if (actionRoutes.Count == 0 && controllerPrefix is not null)
                {
                    foreach (var attribute in methodNode.AttributeLists.SelectMany(list => list.Attributes))
                    {
                        if (!IsRouteAttribute(attribute))
                            continue;

                        var template = ResolveAttributeString(attribute, constants);
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

            var template = ResolveAttributeString(attribute, constants);
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

            var template = ResolveAttributeString(attribute, constants);
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

    private static void ExtractMinimalApiRoutes(
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

            var methods = ResolveHttpMethods(invokedName, invocation, constants);
            if (methods.Count == 0)
                continue;

            var routeTemplate = ResolveMinimalApiTemplate(invocation, constants);
            if (routeTemplate is null)
                continue;

            var prefix = ResolveMapGroupPrefix((SyntaxNode)invocation.Expression, constants);
            var fullRoute = CombineRoutes(prefix, routeTemplate);
            var lineNumber = invocation.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            var sourceId = ResolveMinimalApiSourceId(invocation, relPath, nodes, projectContext);
            var confidence = NormalizeRouteTemplate(fullRoute).Contains("{param}", StringComparison.Ordinal) ? 0.9 : 1.0;

            foreach (var method in methods)
                AddEndpoint(nodes, edges, projectContext, sourceId, method, fullRoute, "aspnet-minimal-api", confidence, relPath, lineNumber);
        }
    }

    private static string ResolveMinimalApiSourceId(
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

    private static ArgumentSyntax? GetMinimalApiHandlerArgument(InvocationExpressionSyntax invocation)
    {
        if (!TryGetInvokedName(invocation, out var invokedName))
            return null;

        var index = invokedName == "MapMethods" ? 2 : 1;
        return invocation.ArgumentList.Arguments.Count > index
            ? invocation.ArgumentList.Arguments[index]
            : null;
    }

    private static string? TryResolveContainingMethodId(
        SyntaxNode node,
        IReadOnlyList<IngestNodeRequest> nodes,
        string relPath)
    {
        var containingMethod = node.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        if (containingMethod is null)
            return null;

        return TryFindMethodId(nodes, relPath, containingMethod);
    }

    private static string? TryFindMethodId(
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

    private static string ShortMethodName(string signature)
    {
        var withoutParams = signature.Split('(')[0];
        var lastDot = withoutParams.LastIndexOf('.');
        return lastDot >= 0 ? withoutParams[(lastDot + 1)..] : withoutParams;
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
        var normalizedRoute = NormalizeRouteTemplate(template);
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

    private static string NormalizeRouteTemplate(string template)
    {
        var normalized = template.Trim();
        if (Uri.TryCreate(normalized, UriKind.Absolute, out var absoluteUri))
            normalized = Uri.UnescapeDataString(absoluteUri.AbsolutePath);

        var queryIndex = normalized.IndexOfAny(['?', '#']);
        if (queryIndex >= 0)
            normalized = normalized[..queryIndex];

        normalized = normalized.Replace('\\', '/');
        normalized = DuplicateSlashRegex().Replace(normalized, "/");
        if (!normalized.StartsWith('/'))
            normalized = "/" + normalized;

        normalized = RoutePlaceholderRegex().Replace(normalized, "{param}");
        normalized = ColonParameterRegex().Replace(normalized, "/{param}");
        normalized = normalized.TrimEnd('/');
        if (normalized.Length == 0)
            normalized = "/";

        return normalized.ToLowerInvariant();
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
                    ResolveMinimalApiTemplate(invocation, constants)),
            MemberAccessExpressionSyntax memberAccess => ResolveMapGroupPrefix(memberAccess.Expression, constants),
            _ => null
        };
    }

    private static string? ResolveMinimalApiTemplate(
        InvocationExpressionSyntax invocation,
        IReadOnlyDictionary<string, string> constants)
    {
        var argumentIndex = TryGetInvokedName(invocation, out var invokedName) && invokedName == "MapMethods"
            ? 0
            : 0;

        if (invocation.ArgumentList.Arguments.Count <= argumentIndex)
            return null;

        return ResolveStringExpression(invocation.ArgumentList.Arguments[argumentIndex].Expression, constants);
    }

    private static List<string> ResolveHttpMethods(
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

    private static string? ResolveAttributeString(AttributeSyntax attribute, IReadOnlyDictionary<string, string> constants)
    {
        if (attribute.ArgumentList is null || attribute.ArgumentList.Arguments.Count == 0)
            return null;

        return ResolveStringExpression(attribute.ArgumentList.Arguments[0].Expression, constants);
    }

    private static Dictionary<string, string> BuildStringConstants(CompilationUnitSyntax root)
    {
        var constants = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var field in root.DescendantNodes().OfType<FieldDeclarationSyntax>())
        {
            if (!field.Modifiers.Any(modifier => modifier.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.ConstKeyword)))
                continue;

            foreach (var variable in field.Declaration.Variables)
            {
                if (variable.Initializer?.Value is { } value && ResolveStringExpression(value, constants) is { } resolved)
                    constants[variable.Identifier.Text] = resolved;
            }
        }

        foreach (var local in root.DescendantNodes().OfType<LocalDeclarationStatementSyntax>())
        {
            if (!local.Modifiers.Any(modifier => modifier.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.ConstKeyword)))
                continue;

            foreach (var variable in local.Declaration.Variables)
            {
                if (variable.Initializer?.Value is { } value && ResolveStringExpression(value, constants) is { } resolved)
                    constants[variable.Identifier.Text] = resolved;
            }
        }

        return constants;
    }

    private static string? ResolveStringExpression(ExpressionSyntax expression, IReadOnlyDictionary<string, string> constants)
    {
        return expression switch
        {
            LiteralExpressionSyntax literal when literal.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.StringLiteralExpression)
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

    private sealed record RouteCandidate(string Method, string Template, string Source, double Confidence);

    [GeneratedRegex("/{2,}")]
    private static partial Regex DuplicateSlashRegex();

    [GeneratedRegex(@"/:[A-Za-z_][A-Za-z0-9_]*")]
    private static partial Regex ColonParameterRegex();

    [GeneratedRegex(@"\{[^}]+\}")]
    private static partial Regex RoutePlaceholderRegex();
}
