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
        List<IngestEdgeRequest> edges,
        CSharpConfigurationConstantRegistry constants)
    {
        foreach (var access in root.DescendantNodes().OfType<ElementAccessExpressionSyntax>())
        {
            var rawKey = ResolveStringExpression(access.ArgumentList.Arguments.SingleOrDefault()?.Expression, constants);
            if (rawKey is null || !LooksLikeConfigurationAccessor(access.Expression))
                continue;

            AddConfigurationUsage(nodes, edges, access, relativePath, projectContext, rawKey, "ReadsConfig", "indexer");
        }

        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (invocation.Expression is MemberAccessExpressionSyntax { Name: GenericNameSyntax genericName } &&
                genericName.Identifier.ValueText == "Get")
            {
                var rawKey = FindSectionKey(invocation, constants);
                var optionsType = genericName.TypeArgumentList.Arguments.FirstOrDefault()?.ToString();
                if (rawKey is not null && optionsType is not null)
                    AddConfigurationUsage(nodes, edges, invocation, relativePath, projectContext, rawKey, "BindsConfig", "Get", optionsType);

                continue;
            }

            var memberName = GetMemberName(invocation.Expression);
            if (memberName is null)
                continue;

            if (memberName is "GetSection" or "GetRequiredSection")
            {
                var rawKey = ResolveStringExpression(invocation.ArgumentList.Arguments.SingleOrDefault()?.Expression, constants);
                if (rawKey is not null)
                    AddConfigurationUsage(nodes, edges, invocation, relativePath, projectContext, rawKey, "ReadsConfig", memberName);

                continue;
            }

            if (memberName == "Configure")
            {
                var rawKey = FindSectionKey(invocation, constants);
                if (rawKey is null)
                    continue;

                var optionsType = GetFirstGenericArgument(invocation);
                AddConfigurationUsage(nodes, edges, invocation, relativePath, projectContext, rawKey, "BindsConfig", "Configure", optionsType);
                continue;
            }

            if (memberName == "Bind")
            {
                var rawKey = FindSectionKey(invocation, constants);
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
        var sourceId = CSharpIndexerSyntaxUtilities.ResolveSourceId(syntaxNode, relativePath, projectContext);
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
            CallSite: $"{relativePath}:{CSharpIndexerSyntaxUtilities.GetLineNumber(syntaxNode)}",
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

    private static string? FindSectionKey(InvocationExpressionSyntax invocation, CSharpConfigurationConstantRegistry constants)
    {
        foreach (var nestedInvocation in invocation.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
        {
            var memberName = GetMemberName(nestedInvocation.Expression);
            if (memberName is not "GetSection" and not "GetRequiredSection")
                continue;

            var rawKey = ResolveStringExpression(nestedInvocation.ArgumentList.Arguments.SingleOrDefault()?.Expression, constants);
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

    private static string? ResolveStringExpression(ExpressionSyntax? expression, CSharpConfigurationConstantRegistry constants)
        => CSharpIndexerSyntaxUtilities.ResolveStringExpression(expression, constants);

    private static bool LooksLikeConfigurationAccessor(ExpressionSyntax expression)
    {
        var text = expression.ToString();
        return text.Contains("config", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("Configuration", StringComparison.Ordinal);
    }

    private static bool LooksSecretLike(string canonicalKey) =>
        canonicalKey.Contains("password", StringComparison.OrdinalIgnoreCase) ||
        canonicalKey.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
        canonicalKey.Contains("token", StringComparison.OrdinalIgnoreCase) ||
        canonicalKey.Contains("apikey", StringComparison.OrdinalIgnoreCase);
}
