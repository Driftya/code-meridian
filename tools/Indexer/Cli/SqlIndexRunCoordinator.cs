using System.Diagnostics;
using System.Text.Json;
using CodeMeridian.RoslynIndexer.Pipeline;
using CodeMeridian.Tooling.Discovery;

namespace CodeMeridian.Indexer.Cli.Commands;

internal static class SqlIndexRunCoordinator
{
    public static bool IsEnabled(ResolvedIndexerSettings settings) =>
        settings.Sql?.Enabled == true && !settings.SkipSql && !settings.ExternalOnly;

    public static DirectoryInfo? ResolveWorkerRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = new DirectoryInfo(Path.Combine(directory.FullName, "tools", "SqlIndexer"));
            if (File.Exists(Path.Combine(candidate.FullName, "package.json"))) return candidate;
        }
        return null;
    }

    public static IReadOnlyList<FileInfo> SelectFiles(DirectoryInfo root) =>
        root.EnumerateFiles("*", SearchOption.AllDirectories)
            .Where(file => file.Extension.Equals(".sql", StringComparison.OrdinalIgnoreCase)
                && !IndexingExclusionPolicy.IsIgnoredPath(root, file)).OrderBy(file => file.FullName, StringComparer.Ordinal).ToArray();

    public static void PrintSelection(ResolvedIndexerSettings settings)
    {
        var enabled = IsEnabled(settings);
        Console.WriteLine($"  SQL / PostgreSQL  : {(enabled ? "enabled" : "disabled (opt-in via indexing.sql.enabled)")}");
        Console.WriteLine("    Native WASM parser: pgsql-parser 18.2.8; PostgreSQL grammar 180004; traversal 18.0.0");
        Console.WriteLine($"    Worker assets: {(ResolveWorkerRoot() is null ? "unavailable" : "available; npm dependencies may need restoring")}");
        if (!enabled || !settings.RootPath.Exists) return;
        Console.WriteLine($"    Dialect: {settings.Sql!.DefaultDialect}; database scope: {settings.Sql.DatabaseScope}");
        foreach (var rule in settings.Sql.Sources ?? [])
            Console.WriteLine($"    Rule: {rule.Pattern} -> {rule.Dialect}; scope: {rule.DatabaseScope ?? settings.Sql.DatabaseScope}");
        foreach (var file in SelectFiles(settings.RootPath))
            Console.WriteLine($"    - {Path.GetRelativePath(settings.RootPath.FullName, file.FullName)}");
    }

    public static async Task<int> RunAsync(ResolvedIndexerSettings settings, DirectoryInfo cacheDirectory, bool force,
        CancellationToken cancellationToken = default)
    {
        var worker = ResolveWorkerRoot() ?? throw new InvalidOperationException("SQL worker assets are missing from this CLI package.");
        // A tsx in another workspace does not establish that SQL parser assets exist.
        if (!File.Exists(Path.Combine(worker.FullName, "node_modules", "pgsql-parser", "package.json"))
            && !File.Exists(Path.Combine(worker.Parent!.Parent!.FullName, "node_modules", "pgsql-parser", "package.json")))
        {
            var restore = await NodeIndexerProcessRunner.RunAsync(ExternalCommandResolver.NpmCommand(),
                ["ci", "--workspaces=false", "--include=dev", "--include=optional"], worker);
            if (restore != 0) return restore;
        }
        var tsx = NodeIndexerProcessRunner.ResolveTsxCommand(worker)
            ?? throw new InvalidOperationException("SQL worker tsx runtime is unavailable.");
        cacheDirectory.Create();
        var batch = Path.Combine(cacheDirectory.FullName, "sql-batch.json");
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var classifier = IndexedFileRoleClassifierFactory.Create(settings.FileRoles);
        await File.WriteAllTextAsync(batch, JsonSerializer.Serialize(SelectFiles(settings.RootPath).Select(file =>
        {
            var relative = Path.GetRelativePath(settings.RootPath.FullName, file.FullName).Replace('\\', '/');
            return new { path = relative, fileRole = classifier.Classify(relative).ToString() };
        }), options), cancellationToken);
        var settingsPath = Path.Combine(cacheDirectory.FullName, "sql-settings.json");
        await File.WriteAllTextAsync(settingsPath, JsonSerializer.Serialize(settings.Sql, options), cancellationToken);
        var arguments = new List<string>
        {
            Path.Combine(worker.FullName, "src", "index.ts"), settings.RootPath.FullName, "--project", settings.Project,
            "--url", settings.CodeMeridianUrl, "--batch-file", batch, "--settings-file", settingsPath,
            "--cache-file", Path.Combine(cacheDirectory.FullName, "sql-fingerprint")
        };
        if (force) arguments.Add("--force");
        using var process = new Process { StartInfo = new ProcessStartInfo(tsx) { WorkingDirectory = worker.FullName, UseShellExecute = false } };
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        if (!string.IsNullOrWhiteSpace(settings.ApiKey)) process.StartInfo.Environment["CODEMERIDIAN_API_KEY"] = settings.ApiKey;
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ConsoleCancelEventHandler cancelHandler = (_, args) => { args.Cancel = true; linkedCancellation.Cancel(); };
        Console.CancelKeyPress += cancelHandler;
        process.Start();
        try { await process.WaitForExitAsync(linkedCancellation.Token); }
        catch (OperationCanceledException) { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
        finally { Console.CancelKeyPress -= cancelHandler; }
        return process.ExitCode;
    }
}
