using CodeMeridian.Core.CodeGraph;
using Microsoft.CodeAnalysis;

namespace CodeMeridian.RoslynIndexer.Pipeline;

internal static class PackageTypeForwarding
{
    public static IReadOnlyDictionary<string, IReadOnlyList<PackageForwardingAssembly>> Build(
        Compilation compilation, Func<string?, PackageAsset?> findAsset)
    {
        var result = new Dictionary<string, List<PackageForwardingAssembly>>(StringComparer.Ordinal);
        foreach (var reference in compilation.References.OfType<PortableExecutableReference>())
        {
            if (findAsset(reference.FilePath) is not { } asset
                || compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol assembly) continue;
            foreach (var type in assembly.GetForwardedTypes())
            {
                if (PackageSymbolIdentity.Key(type) is not { } key) continue;
                var identity = type.ContainingAssembly.Identity + "::" + key;
                if (!result.TryGetValue(identity, out var origins)) result[identity] = origins = [];
                if (origins.Count < 10) origins.Add(new(assembly.Identity.ToString(), asset.Id, asset.Version, asset.CompileAsset));
            }
        }
        return result.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<PackageForwardingAssembly>)pair.Value.Distinct().ToArray());
    }
}
