namespace CodeMeridian.Indexer.Cli;

internal static class InitCommand
{
    public static int Run(DirectoryInfo rootPath, string? projectOverride, string codeMeridianUrl, bool force)
    {
        Directory.CreateDirectory(rootPath.FullName);

        var project = projectOverride
            ?? IndexerConfig.Load(rootPath)?.Project
            ?? IndexerDiscovery.ResolveProjectName(rootPath);

        try
        {
            IndexerConfig.Write(rootPath, project, codeMeridianUrl, overwrite: force);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }

        var configPath = Path.Combine(rootPath.FullName, "meridian.json");
        Console.WriteLine("Initialized CodeMeridian indexer config:");
        Console.WriteLine($"  Path    : {configPath}");
        Console.WriteLine($"  Project : {project}");
        Console.WriteLine($"  Server  : {codeMeridianUrl}");
        Console.WriteLine();
        Console.WriteLine("Next step:");
        Console.WriteLine("  codemeridian index .");

        return 0;
    }
}
