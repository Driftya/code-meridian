namespace CodeMeridian.Indexer.Cli;

internal static class InitCommand
{
    public static int Run(DirectoryInfo rootPath, string? projectOverride, string codeMeridianUrl, bool force)
    {
        Directory.CreateDirectory(rootPath.FullName);

        var project = projectOverride
            ?? IndexerConfig.LoadLocal(rootPath)?.Project
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
        var clientConfigChanges = new ServeWriter().ApplyClientConfig(rootPath, codeMeridianUrl, force);

        Console.WriteLine("Initialized CodeMeridian indexer config:");
        Console.WriteLine($"  Path    : {configPath}");
        Console.WriteLine($"  Project : {project}");
        Console.WriteLine($"  Server  : {codeMeridianUrl}");
        Console.WriteLine();
        Console.WriteLine("Client MCP config:");
        foreach (var change in clientConfigChanges)
            Console.WriteLine($"  {change.Status,-11} {change.Path}");
        Console.WriteLine();
        Console.WriteLine("Next step:");
        Console.WriteLine("  codemeridian index .");

        return 0;
    }

    public static int RunGlobal(string codeMeridianUrl, bool force, DirectoryInfo? globalConfigDirectory = null)
    {
        try
        {
            IndexerConfig.WriteGlobal(codeMeridianUrl, overwrite: force, globalConfigDirectory);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }

        var configPath = IndexerConfig.GetGlobalConfigFile(globalConfigDirectory).FullName;

        Console.WriteLine("Initialized global CodeMeridian indexer config:");
        Console.WriteLine($"  Path   : {configPath}");
        Console.WriteLine($"  Server : {codeMeridianUrl}");
        Console.WriteLine();
        Console.WriteLine("Project names remain auto-detected unless a project-local meridian.json, .env, or --project overrides them.");
        Console.WriteLine();
        Console.WriteLine("Next step:");
        Console.WriteLine("  codemeridian index .");

        return 0;
    }
}
