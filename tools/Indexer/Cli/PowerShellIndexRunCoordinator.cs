using System.Text.Json;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Tooling.Discovery;

namespace CodeMeridian.Indexer.Cli.Commands;

internal static class PowerShellIndexRunCoordinator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public static bool IsEnabled(ResolvedIndexerSettings settings) => settings.PowerShell?.Enabled == true
        && !settings.SkipPowerShell && !settings.ExternalOnly;

    public static bool IsSourcePath(string path) => Path.GetExtension(path).ToLowerInvariant() is ".ps1" or ".psm1" or ".psd1";

    public static IReadOnlyList<FileInfo> SelectFiles(DirectoryInfo root) => new RepositoryInventoryDiscovery().Discover(root).Files
        .Where(IsSourcePath).Select(path => new FileInfo(Path.Combine(root.FullName, path))).ToArray();

    public static void PrintSelection(ResolvedIndexerSettings settings)
    {
        Console.WriteLine($"  PowerShell       : {(IsEnabled(settings) ? "enabled; full selection and rebind on every pass" : "disabled (opt-in via indexing.powershell.enabled)")}");
        Console.WriteLine($"    Isolated worker: {(PowerShellWorkerProcess.ResolveExecutable() is null ? "unavailable" : "available")}; query coverage remains unknown");
        if (IsEnabled(settings) && settings.RootPath.Exists)
            foreach (var file in SelectFiles(settings.RootPath)) Console.WriteLine($"    - {Path.GetRelativePath(settings.RootPath.FullName, file.FullName)}");
    }

    public static async Task<int> RunAsync(ResolvedIndexerSettings settings, IndexProfileRun report,
        CancellationToken cancellationToken = default)
    {
        var fingerprint = report.Fingerprint("powershell") ?? throw new InvalidOperationException("PowerShell worker identity is unavailable.");
        var paths = report.PowerShellPaths;
        var sensitive = settings.PowerShell?.CaseSensitive ?? report.CaseSensitive
            ?? throw new InvalidOperationException("Uniform filesystem case semantics are unknown. Set indexing.powershell.caseSensitive explicitly for this repository.");
        if (report.CaseSensitive is { } observed && sensitive != observed)
            throw new InvalidOperationException("Configured PowerShell path case policy conflicts with the observed filesystem semantics.");
        var requestPath = Path.Combine(Path.GetTempPath(), "codemeridian-powershell-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            await File.WriteAllTextAsync(requestPath, JsonSerializer.Serialize(new
            {
                contractVersion = "1.0", project = settings.Project, rootPath = settings.RootPath.FullName,
                caseSensitive = sensitive, files = paths
            }, JsonOptions), cancellationToken);
            // Always analyze all selected files: imports/exports/deletions can rebind unchanged callers.
            var (exitCode, output) = await PowerShellWorkerProcess.RunAsync(["graph", requestPath], 16 * 1024 * 1024, cancellationToken);
            if (exitCode is not (0 or 2)) return exitCode;
            using var json = JsonDocument.Parse(output);
            var root = json.RootElement;
            if (root.GetProperty("contractVersion").GetString() != "1.0" || root.GetProperty("project").GetString() != settings.Project
                || (root.GetProperty("status").GetString() == "complete") != (exitCode == 0)
                || PowerShellWorkerProcess.ParseFingerprint(root.GetProperty("worker")) != fingerprint)
                throw new InvalidDataException("PowerShell worker protocol or executing identity changed during analysis.");
            var files = root.GetProperty("files").Deserialize<PowerShellFileGraph[]>(JsonOptions)
                ?? throw new InvalidDataException("PowerShell worker returned no file outcomes.");
            var snapshot = new PowerShellGraphSnapshot(settings.Project, report.Generation, report.SourceFingerprint, fingerprint, files);
            if (snapshot.Validate() is { } error) throw new InvalidDataException(error);
            await report.PublishPowerShellAsync(snapshot, cancellationToken);
            return exitCode == 0 && files.All(file => file.Status == "complete") ? 0 : 2;
        }
        finally { File.Delete(requestPath); }
    }
}
