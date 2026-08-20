using System.Xml;
using System.Xml.Linq;

namespace CodeMeridian.RoslynIndexer.Pipeline;

/// <summary>
/// Resolves the C# project directories that are explicitly part of an index run.
/// In addition to projects below the supplied root, this includes projects reached
/// through <c>ProjectReference</c> and solution XML (<c>.slnx</c>) entries.
/// </summary>
public static class CSharpProjectScope
{
    public static IReadOnlyList<DirectoryInfo> ResolveProjectRoots(DirectoryInfo root)
    {
        var projects = new Queue<FileInfo>(EnumerateProjects(root));
        var seenProjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var roots = new Dictionary<string, DirectoryInfo>(StringComparer.OrdinalIgnoreCase)
        {
            [Path.GetFullPath(root.FullName)] = root
        };

        foreach (var solution in SafeEnumerateFiles(root, "*.slnx", SearchOption.AllDirectories))
            EnqueueReferencedProjects(solution.Directory!, ReadSlnxProjectPaths(solution), projects);

        while (projects.TryDequeue(out var project))
        {
            var projectPath = Path.GetFullPath(project.FullName);
            if (!seenProjects.Add(projectPath) || !project.Exists)
                continue;

            roots[project.Directory!.FullName] = project.Directory;
            EnqueueReferencedProjects(project.Directory, ReadProjectReferencePaths(project), projects);
        }

        return roots.Values
            .OrderBy(directory => directory.FullName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<FileInfo> EnumerateCSharpFiles(DirectoryInfo root)
    {
        return ResolveProjectRoots(root)
            .SelectMany(projectRoot => SafeEnumerateFiles(projectRoot, "*.cs", SearchOption.AllDirectories))
            .Where(file => !IsIgnored(file))
            .DistinctBy(file => Path.GetFullPath(file.FullName), StringComparer.OrdinalIgnoreCase)
            .OrderBy(file => file.FullName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<FileInfo> EnumerateProjects(DirectoryInfo root) =>
        SafeEnumerateFiles(root, "*.csproj", SearchOption.AllDirectories)
            .Where(project => !IsIgnored(project));

    private static IEnumerable<string> ReadProjectReferencePaths(FileInfo project) =>
        ReadXml(project)
            .Descendants()
            .Where(element => element.Name.LocalName == "ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .OfType<string>();

    private static IEnumerable<string> ReadSlnxProjectPaths(FileInfo solution) =>
        ReadXml(solution)
            .Descendants()
            .Where(element => element.Name.LocalName == "Project")
            .Select(element => element.Attribute("Path")?.Value)
            .OfType<string>();

    private static XDocument ReadXml(FileInfo file)
    {
        try
        {
            using var stream = file.OpenRead();
            using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
            return XDocument.Load(reader);
        }
        catch (XmlException) { return new XDocument(); }
        catch (IOException) { return new XDocument(); }
        catch (UnauthorizedAccessException) { return new XDocument(); }
    }

    private static void EnqueueReferencedProjects(DirectoryInfo directory, IEnumerable<string> references, Queue<FileInfo> projects)
    {
        foreach (var reference in references)
        {
            if (string.IsNullOrWhiteSpace(reference) || reference.Contains('$'))
                continue;

            var path = Path.GetFullPath(reference, directory.FullName);
            if (path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) && File.Exists(path))
                projects.Enqueue(new FileInfo(path));
        }
    }

    private static IEnumerable<FileInfo> SafeEnumerateFiles(DirectoryInfo root, string pattern, SearchOption searchOption)
    {
        try { return root.EnumerateFiles(pattern, searchOption); }
        catch (UnauthorizedAccessException) { return []; }
        catch (DirectoryNotFoundException) { return []; }
        catch (IOException) { return []; }
    }

    private static bool IsIgnored(FileInfo file)
    {
        var segments = file.FullName.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        return segments.Any(segment =>
            segment.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals(".vs", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals(".vscode", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals(".meridian", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("node_modules", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("dist", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("build", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("coverage", StringComparison.OrdinalIgnoreCase));
    }
}
