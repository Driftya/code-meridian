using Microsoft.CodeAnalysis;

namespace CodeMeridian.RoslynIndexer.Pipeline;

internal static class PackageSymbolIdentity
{
    public static ISymbol Definition(ISymbol symbol) => symbol is IMethodSymbol method
        ? (method.ReducedFrom ?? method).OriginalDefinition : symbol.OriginalDefinition;

    public static string? Key(ISymbol symbol)
    {
        var definition = Definition(symbol);
        var declaration = definition.GetDocumentationCommentId();
        if (declaration is null) return null;
        // Documentation IDs encode overload types and generic arity, but ref/out share '@'.
        var modifiers = definition is IMethodSymbol method
            ? string.Join(",", method.Parameters.Select(parameter => parameter.RefKind.ToString())) : "";
        return "csharp.symbol.v1:" + declaration + "|" + modifiers;
    }

    public static string Id(params string[] parts) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes(string.Join("\0", parts)))).ToLowerInvariant();
}
