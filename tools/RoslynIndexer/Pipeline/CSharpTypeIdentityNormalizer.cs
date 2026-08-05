namespace CodeMeridian.RoslynIndexer.Pipeline;

internal sealed record CSharpTypeIdentity(string CanonicalName, string ShortName);

internal static class CSharpTypeIdentityNormalizer
{
    private static readonly IReadOnlyDictionary<string, string> CanonicalAliases =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["System.Boolean"] = "bool",
            ["System.Byte"] = "byte",
            ["System.Char"] = "char",
            ["System.Decimal"] = "decimal",
            ["System.Double"] = "double",
            ["System.Int16"] = "short",
            ["System.Int32"] = "int",
            ["System.Int64"] = "long",
            ["System.Object"] = "object",
            ["System.SByte"] = "sbyte",
            ["System.Single"] = "float",
            ["System.String"] = "string",
            ["System.UInt16"] = "ushort",
            ["System.UInt32"] = "uint",
            ["System.UInt64"] = "ulong",
            ["System.Void"] = "void"
        };

    public static CSharpTypeIdentity? Normalize(string? rawType)
    {
        if (string.IsNullOrWhiteSpace(rawType))
            return null;

        var canonical = rawType.Trim();
        foreach (var modifier in new[] { "scoped ", "ref ", "out ", "in " })
        {
            if (canonical.StartsWith(modifier, StringComparison.Ordinal))
                canonical = canonical[modifier.Length..].TrimStart();
        }

        canonical = canonical.Replace("global::", string.Empty, StringComparison.Ordinal);
        canonical = RemoveGenericArguments(canonical).Trim();
        while (canonical.EndsWith("?", StringComparison.Ordinal))
            canonical = canonical[..^1].TrimEnd();
        while (TryRemoveArraySuffix(canonical, out var elementType))
            canonical = elementType;

        if (canonical.Length == 0)
            return null;

        if (CanonicalAliases.TryGetValue(canonical, out var alias))
            canonical = alias;

        var separator = Math.Max(canonical.LastIndexOf('.'), canonical.LastIndexOf('+'));
        var shortName = separator >= 0 ? canonical[(separator + 1)..] : canonical;
        return new CSharpTypeIdentity(canonical, shortName);
    }

    private static string RemoveGenericArguments(string value)
    {
        var genericStart = value.IndexOf('<');
        if (genericStart < 0)
            return value;

        return value[..genericStart];
    }

    private static bool TryRemoveArraySuffix(string value, out string elementType)
    {
        elementType = value;
        if (!value.EndsWith(']'))
            return false;

        var openBracket = value.LastIndexOf('[');
        if (openBracket < 0)
            return false;

        var rank = value[(openBracket + 1)..^1];
        if (rank.Any(character => character != ',' && !char.IsWhiteSpace(character)))
            return false;

        elementType = value[..openBracket].TrimEnd();
        return true;
    }
}
