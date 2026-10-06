using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using CodeMeridian.Core.CodeGraph;

namespace CodeMeridian.RoslynIndexer.Pipeline;

internal sealed class PackageAssetCatalog
{
    private readonly Dictionary<string, PackageAsset> _assets = new(StringComparer.OrdinalIgnoreCase);
    public PackageAsset? Find(string? path) => path is null ? null : _assets.GetValueOrDefault(Path.GetFullPath(path));

    public static PackageAssetCatalog Read(string project, string framework)
    {
        var catalog = new PackageAssetCatalog();
        var path = Path.Combine(Path.GetDirectoryName(project)!, "obj", "project.assets.json");
        if (!File.Exists(path)) throw new InvalidOperationException("Restore assets are unavailable; restore the project before indexing package references.");
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        var root = json.RootElement;
        var targets = root.GetProperty("targets");
        // The compile target is framework-specific; never merge RID/framework variants by DLL filename.
        var target = targets.EnumerateObject().FirstOrDefault(item => item.Name == framework);
        if (target.Value.ValueKind == JsonValueKind.Undefined)
        {
            var aliases = root.GetProperty("project").GetProperty("frameworks").EnumerateObject()
                .Where(item => item.Value.TryGetProperty("targetAlias", out var alias) && alias.GetString() == framework)
                .Select(item => item.Name).ToArray();
            target = targets.EnumerateObject().FirstOrDefault(item => aliases.Contains(item.Name));
        }
        if (target.Value.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("Selected framework restore target is unavailable.");
        foreach (var library in target.Value.EnumerateObject())
        {
            var metadata = root.GetProperty("libraries").GetProperty(library.Name);
            if (metadata.GetProperty("type").GetString() != "package" || !library.Value.TryGetProperty("compile", out var compile)) continue;
            var separator = library.Name.LastIndexOf('/');
            var id = library.Name[..separator];
            var version = library.Name[(separator + 1)..];
            foreach (var folder in root.GetProperty("packageFolders").EnumerateObject())
            {
                var packagePath = Path.Combine(folder.Name, metadata.GetProperty("path").GetString()!);
                var repository = ReadRepository(packagePath, id);
                foreach (var asset in compile.EnumerateObject().Where(asset => asset.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
                {
                    var assembly = Path.GetFullPath(Path.Combine(packagePath, asset.Name));
                    var segments = asset.Name.Split('/');
                    if (File.Exists(assembly)) catalog._assets[assembly] = new(id, version, repository.Url, repository.Commit,
                        asset.Name, segments.Length > 2 ? segments[1] : null);
                }
            }
        }
        return catalog;
    }

    private static (string? Url, string? Commit) ReadRepository(string directory, string id)
    {
        var nuspec = Path.Combine(directory, id.ToLowerInvariant() + ".nuspec");
        if (!File.Exists(nuspec)) return (null, null);
        using var reader = XmlReader.Create(nuspec, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
        var repository = XDocument.Load(reader).Descendants().FirstOrDefault(element => element.Name.LocalName == "repository");
        return (PackageRepositoryIdentity.Normalize(repository?.Attribute("url")?.Value), repository?.Attribute("commit")?.Value);
    }
}

internal sealed record PackageAsset(string Id, string Version, string? RepositoryUrl, string? Commit, string CompileAsset, string? Framework);
