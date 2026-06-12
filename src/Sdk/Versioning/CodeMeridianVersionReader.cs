using System.Reflection;

namespace CodeMeridian.Sdk.Versioning;

public static class CodeMeridianVersionReader
{
    public static CodeMeridianComponentVersion ReadFrom(Assembly assembly, string component)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentException.ThrowIfNullOrWhiteSpace(component);

        return new CodeMeridianComponentVersion(
            component,
            assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0-unknown",
            ReadIntegerMetadata(assembly, "GraphContractVersion"),
            ReadIntegerMetadata(assembly, "CacheVersion"));
    }

    private static int ReadIntegerMetadata(Assembly assembly, string key)
    {
        var raw = assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => string.Equals(attribute.Key, key, StringComparison.Ordinal))
            ?.Value;

        return int.TryParse(raw, out var value) ? value : 0;
    }
}
