namespace CodeMeridian.Indexer.Cli;

internal static class ExternalCommandResolver
{
    public static string NpmCommand() => ResolveCommandFromPath(OperatingSystem.IsWindows() ? "npm.cmd" : "npm");

    public static string ResolveCommandFromPath(string command)
    {
        if (Path.IsPathRooted(command))
            return command;

        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
            return command;

        foreach (var directory in path.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory))
                continue;

            try
            {
                var candidate = Path.Combine(directory.Trim('"'), command);
                if (File.Exists(candidate))
                    return candidate;
            }
            catch
            {
                // Ignore malformed PATH entries.
            }
        }

        return command;
    }
}
