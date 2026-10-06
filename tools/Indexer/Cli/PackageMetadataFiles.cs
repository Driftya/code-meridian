namespace CodeMeridian.Indexer.Cli.Commands;

internal static class PackageMetadataFiles
{
    public static IReadOnlyList<FileInfo> Enumerate(DirectoryInfo root)
    {
        var projectInputs = root.EnumerateFiles("*.*", SearchOption.AllDirectories)
            .Where(file => !IndexExecutionPlanBuilder.IsIgnoredPath(root, file))
            .Where(IsMetadata).ToList();
        foreach (var project in projectInputs.Where(file => file.Extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase)).ToArray())
        {
            var assets = new FileInfo(Path.Combine(project.DirectoryName!, "obj", "project.assets.json"));
            if (assets.Exists) projectInputs.Add(assets);
        }
        var git = new DirectoryInfo(Path.Combine(root.FullName, ".git"));
        if (git.Exists)
        {
            foreach (var name in new[] { "HEAD", "index", "packed-refs" })
            {
                var file = new FileInfo(Path.Combine(git.FullName, name));
                if (file.Exists) projectInputs.Add(file);
            }
            var refs = new DirectoryInfo(Path.Combine(git.FullName, "refs", "heads"));
            if (refs.Exists) projectInputs.AddRange(refs.EnumerateFiles("*", SearchOption.AllDirectories));
        }
        return projectInputs;
    }

    public static bool IsMetadata(FileInfo file) => file.Extension.ToLowerInvariant() is ".csproj" or ".props" or ".targets"
        || file.Name.ToLowerInvariant() is "global.json" or "nuget.config" or "packages.lock.json" or "project.assets.json" or "meridian.json"
        || file.FullName.Replace('\\', '/').Contains("/.git/", StringComparison.OrdinalIgnoreCase);
}
