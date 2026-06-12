using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMeridian.RoslynIndexer.Pipeline;

internal static class CSharpConfigurationUsageExtractor
{
    public static void Extract(
        CompilationUnitSyntax root,
        string relativePath,
        string projectContext,
        List<IngestNodeRequest> nodes,
        List<IngestEdgeRequest> edges)
    {
        foreach (var access in root.DescendantNodes().OfType<ElementAccessExpressionSyntax>())
        {
            var rawKey = TryGetStringLiteral(access.ArgumentList.Arguments.SingleOrDefault()?.Expression);
            if (rawKey is null || !LooksLikeConfigurationAccessor(access.Expression))
                continue;

            AddConfigurationUsage(nodes, edges, access, relativePath, projectContext, rawKey, "ReadsConfig", "indexer");
        }

        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var memberName = GetMemberName(invocation.Expression);
            if (memberName is null)
                continue;

            if (memberName is "GetSection" or "GetRequiredSection")
            {
                var rawKey = TryGetStringLiteral(invocation.ArgumentList.Arguments.SingleOrDefault()?.Expression);
                if (rawKey is not null)
                    AddConfigurationUsage(nodes, edges, invocation, relativePath, projectContext, rawKey, "ReadsConfig", memberName);

                continue;
            }

            if (memberName == "Configure")
            {
                var rawKey = FindSectionKey(invocation);
                if (rawKey is null)
                    continue;

                var optionsType = GetFirstGenericArgument(invocation);
                AddConfigurationUsage(nodes, edges, invocation, relativePath, projectContext, rawKey, "BindsConfig", "Configure", optionsType);
                continue;
            }

            if (memberName == "Bind")
            {
                var rawKey = FindSectionKey(invocation);
                if (rawKey is null)
                    continue;

                AddConfigurationUsage(nodes, edges, invocation, relativePath, projectContext, rawKey, "BindsConfig", "Bind");
            }
        }
    }

    private static void AddConfigurationUsage(
        List<IngestNodeRequest> nodes,
        List<IngestEdgeRequest> edges,
        SyntaxNode syntaxNode,
        string relativePath,
        string projectContext,
        string rawKey,
        string relationshipType,
        string accessPattern,
        string? optionsType = null)
    {
        var canonicalKey = rawKey.Replace("__", ":", StringComparison.Ordinal).Trim().Trim('"', '\'');
        var sourceId = ResolveSourceId(syntaxNode, relativePath, projectContext);
        var keyId = $"{projectContext}::ConfigurationKey::{canonicalKey}";

        nodes.Add(new IngestNodeRequest(
            keyId,
            canonicalKey,
            "ConfigurationKey",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["canonicalKey"] = canonicalKey,
                ["normalizedKey"] = canonicalKey.ToLowerInvariant(),
                ["isSecretLike"] = LooksSecretLike(canonicalKey) ? "true" : "false"
            }));

        edges.Add(new IngestEdgeRequest(
            sourceId,
            keyId,
            relationshipType,
            CallSite: $"{relativePath}:{GetLineNumber(syntaxNode)}",
            Confidence: relationshipType == "ReadsConfig" ? 0.95d : 0.9d,
            Properties: BuildProperties(rawKey, accessPattern, optionsType)));
    }

    private static Dictionary<string, string> BuildProperties(string rawKey, string accessPattern, string? optionsType)
    {
        var properties = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["rawKey"] = rawKey,
            ["accessPattern"] = accessPattern
        };

        if (!string.IsNullOrWhiteSpace(optionsType))
            properties["optionsType"] = optionsType;

        return properties;
    }

    private static string ResolveSourceId(SyntaxNode syntaxNode, string relativePath, string projectContext)
    {
        if (syntaxNode.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault() is { } method)
            return BuildMethodId(method, projectContext);

        if (syntaxNode.Ancestors().OfType<ConstructorDeclarationSyntax>().FirstOrDefault() is { } ctor)
            return BuildConstructorId(ctor, projectContext);

        if (syntaxNode.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault() is { } classDeclaration)
            return BuildTypeId("Class", classDeclaration.Identifier.Text, classDeclaration, projectContext);

        return $"{projectContext}::File::{relativePath}";
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

    private static string? FindSectionKey(InvocationExpressionSyntax invocation)
    {
        foreach (var nestedInvocation in invocation.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
        {
            var memberName = GetMemberName(nestedInvocation.Expression);
            if (memberName is not "GetSection" and not "GetRequiredSection")
                continue;

            var rawKey = TryGetStringLiteral(nestedInvocation.ArgumentList.Arguments.SingleOrDefault()?.Expression);
            if (rawKey is not null)
                return rawKey;
        }

        return null;
    }

    private static string? GetFirstGenericArgument(InvocationExpressionSyntax invocation) =>
        invocation.Expression is MemberAccessExpressionSyntax { Name: GenericNameSyntax genericName }
            ? genericName.TypeArgumentList.Arguments.FirstOrDefault()?.ToString()
            : null;

    private static string? GetMemberName(ExpressionSyntax expression) =>
        expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
            MemberBindingExpressionSyntax memberBinding => memberBinding.Name.Identifier.ValueText,
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            _ => null
        };

    private static string? TryGetStringLiteral(ExpressionSyntax? expression) =>
        expression switch
        {
            LiteralExpressionSyntax literal when literal.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.StringLiteralExpression) => literal.Token.ValueText,
            _ => null
        };

    private static bool LooksLikeConfigurationAccessor(ExpressionSyntax expression)
    {
        var text = expression.ToString();
        return text.Contains("config", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Configuration", StringComparison.Ordinal);
    }

    private static int GetLineNumber(SyntaxNode syntaxNode) =>
        syntaxNode.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

    private static bool LooksSecretLike(string canonicalKey) =>
        canonicalKey.Contains("password", StringComparison.OrdinalIgnoreCase) ||
        canonicalKey.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
        canonicalKey.Contains("token", StringComparison.OrdinalIgnoreCase) ||
        canonicalKey.Contains("apikey", StringComparison.OrdinalIgnoreCase);
}
