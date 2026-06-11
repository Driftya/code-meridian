using Microsoft.Extensions.Configuration;

namespace CodeMeridian.Tooling.Discovery;

public sealed class ProjectDiscoveryService : IProjectDiscoveryService
{
    public bool ContainsFile(DirectoryInfo root, params string[] extensions)
    {
        var pending = new Stack<DirectoryInfo>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (ShouldSkipDirectory(current))
                continue;

            foreach (var file in SafeEnumerateFiles(current))
            {
                if (extensions.Contains(file.Extension, StringComparer.OrdinalIgnoreCase)
                    && !file.Name.EndsWith(".d.ts", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            foreach (var directory in SafeEnumerateDirectories(current))
                pending.Push(directory);
        }

        return false;
    }

    public IReadOnlyList<DirectoryInfo> FindTypeScriptRoots(DirectoryInfo root)
    {
        var roots = new List<DirectoryInfo>();

        foreach (var directory in EnumerateDirectoriesDepthFirst(root))
        {
            if (ShouldSkipDirectory(directory))
                continue;

            if (File.Exists(Path.Combine(directory.FullName, "tsconfig.json"))
                && ContainsFile(directory, ".ts", ".tsx"))
            {
                roots.Add(directory);
            }
        }

        if (roots.Count == 0 && ContainsFile(root, ".ts", ".tsx"))
            roots.Add(root);

        return roots
            .Where(candidate => !roots.Any(other =>
                !ReferenceEquals(candidate, other) && IsSubdirectoryOf(candidate, other)))
            .OrderBy(directory => directory.FullName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public DirectoryInfo? FindRepositoryRoot(DirectoryInfo start)
    {
        for (var current = start; current is not null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "CodeMeridian.sln")))
                return current;
        }

        return null;
    }

    public string ResolveProjectName(DirectoryInfo root)
    {
        var packageJson = new FileInfo(Path.Combine(root.FullName, "package.json"));
        if (packageJson.Exists)
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(root.FullName)
                .AddJsonFile(packageJson.Name, optional: false, reloadOnChange: false)
                .Build();

            var name = configuration["name"];
            if (!string.IsNullOrWhiteSpace(name))
                return name.Trim();
        }

        var sln = root.GetFiles("*.sln").FirstOrDefault();
        if (sln is not null) return Path.GetFileNameWithoutExtension(sln.Name);

        var slnx = root.GetFiles("*.slnx").FirstOrDefault();
        if (slnx is not null) return Path.GetFileNameWithoutExtension(slnx.Name);

        var workspace = root.GetFiles("*.code-workspace").FirstOrDefault();
        if (workspace is not null) return Path.GetFileNameWithoutExtension(workspace.Name);

        return root.Name;
    }

    private static bool ShouldSkipDirectory(DirectoryInfo directory)
    {
        var name = directory.Name;
        return name is ".git" or ".vs" or ".vscode" or "bin" or "obj" or "node_modules" or "dist" or "build" or "coverage";
    }

    private static IEnumerable<DirectoryInfo> EnumerateDirectoriesDepthFirst(DirectoryInfo root)
    {
        var pending = new Stack<DirectoryInfo>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            yield return current;

            if (ShouldSkipDirectory(current))
                continue;

            foreach (var directory in SafeEnumerateDirectories(current))
                pending.Push(directory);
        }
    }

    private static bool IsSubdirectoryOf(DirectoryInfo candidate, DirectoryInfo parent)
    {
        var relative = Path.GetRelativePath(parent.FullName, candidate.FullName);
        return relative != "."
            && !relative.StartsWith("..", StringComparison.Ordinal)
            && !Path.IsPathRooted(relative);
    }

    private static IEnumerable<FileInfo> SafeEnumerateFiles(DirectoryInfo directory)
    {
        try { return directory.EnumerateFiles(); }
        catch (UnauthorizedAccessException) { return []; }
        catch (DirectoryNotFoundException) { return []; }
    }

    private static IEnumerable<DirectoryInfo> SafeEnumerateDirectories(DirectoryInfo directory)
    {
        try { return directory.EnumerateDirectories(); }
        catch (UnauthorizedAccessException) { return []; }
        catch (DirectoryNotFoundException) { return []; }
    }
}
