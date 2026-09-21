using System.Text.Json;
using System.Xml.Linq;

namespace CodeMeridian.RoslynIndexer.Pipeline;

/// <summary>Reads existing project/restore metadata without executing MSBuild or restoring packages.</summary>
internal sealed record CSharpSemanticProjectInputs(bool UseCommonImplicitUsings, IReadOnlyList<string> AssemblyPaths)
{
    public static CSharpSemanticProjectInputs Read(IEnumerable<FileInfo> files)
    {
        var cache = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var projects = files.Select(file => FindProject(file.Directory!, cache)).Distinct().ToArray();
        var knownProjects = projects.OfType<string>().ToArray();
        // The current catalog is one synthetic compilation. Do not leak implicit imports into
        // a project that disables them, or into loose source files with no project metadata.
        var implicitUsings = projects.Length > 0 && projects.All(project => project is not null && EnablesImplicitUsings(project));
        var assemblies = knownProjects.SelectMany(ReadPackageAssemblies)
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToArray();
        return new CSharpSemanticProjectInputs(implicitUsings, assemblies);
    }

    private static string? FindProject(DirectoryInfo directory, Dictionary<string, string?> cache)
    {
        if (cache.TryGetValue(directory.FullName, out var cached)) return cached;
        var projects = directory.GetFiles("*.csproj");
        var result = projects.Length == 1 ? projects[0].FullName
            : projects.Length > 1 || directory.Parent is null ? null
            : FindProject(directory.Parent, cache);
        cache[directory.FullName] = result;
        return result;
    }

    private static bool EnablesImplicitUsings(string project)
    {
        try
        {
            var document = XDocument.Load(project);
            var sdk = document.Root?.Attribute("Sdk")?.Value;
            if (sdk is null || !sdk.StartsWith("Microsoft.NET.Sdk", StringComparison.Ordinal)) return false;
            var declarations = document.Descendants().Where(element => element.Name.LocalName == "ImplicitUsings").ToArray();
            return declarations.Length == 1
                && !declarations[0].AncestorsAndSelf().Any(element => element.Attribute("Condition") is not null)
                && declarations[0].Value.Trim() is "enable" or "true";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return false;
        }
    }

    private static IReadOnlyList<string> ReadPackageAssemblies(string project)
    {
        var assetsPath = Path.Combine(Path.GetDirectoryName(project)!, "obj", "project.assets.json");
        if (!File.Exists(assetsPath)) return [];
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(assetsPath));
            var root = document.RootElement;
            if (!root.TryGetProperty("targets", out var targets) || targets.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("libraries", out var libraries) || libraries.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("packageFolders", out var folders) || folders.ValueKind != JsonValueKind.Object)
                return [];
            var result = new List<string>();
            foreach (var target in targets.EnumerateObject())
            {
                if (target.Value.ValueKind != JsonValueKind.Object) continue;
                foreach (var library in target.Value.EnumerateObject())
                {
                    if (library.Value.ValueKind != JsonValueKind.Object
                        || !library.Value.TryGetProperty("compile", out var compile) || compile.ValueKind != JsonValueKind.Object
                        || !libraries.TryGetProperty(library.Name, out var metadata) || metadata.ValueKind != JsonValueKind.Object
                        || !metadata.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String || type.GetString() != "package"
                        || !metadata.TryGetProperty("path", out var packagePath) || packagePath.ValueKind != JsonValueKind.String)
                        continue;
                    foreach (var asset in compile.EnumerateObject().Where(asset => asset.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
                    {
                        foreach (var folder in folders.EnumerateObject())
                        {
                            var path = Path.Combine(folder.Name, packagePath.GetString()!, asset.Name);
                            if (!File.Exists(path)) continue;
                            result.Add(path);
                            break;
                        }
                    }
                }
            }
            return result;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }
}
