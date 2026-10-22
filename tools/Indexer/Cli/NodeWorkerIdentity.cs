using System.Diagnostics;
using System.Text.Json;
using CodeMeridian.Core.Projects;
using CodeMeridian.Tooling.Discovery;

namespace CodeMeridian.Indexer.Cli.Commands;

internal static class NodeWorkerIdentity
{
    public const string ExpectedFingerprintVariable = "CODEMERIDIAN_EXPECTED_WORKER_FINGERPRINT";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static bool IsWorker(string analyzer) => analyzer is "typescript" or "frontend" or "sql";

    public static DirectoryInfo? ResolveRoot(string analyzer, IProjectDiscoveryService? discovery = null)
    {
        var name = analyzer switch { "typescript" => "TsIndexer", "frontend" => "HtmlCssIndexer", "sql" => "SqlIndexer", _ => null };
        if (name is null) return null;
        discovery ??= new ProjectDiscoveryService();
        var repository = discovery.FindRepositoryRoot(new DirectoryInfo(Directory.GetCurrentDirectory()))
            ?? discovery.FindRepositoryRoot(new DirectoryInfo(AppContext.BaseDirectory));
        if (repository is not null)
        {
            var source = new DirectoryInfo(Path.Combine(repository.FullName, "tools", name));
            if (File.Exists(Path.Combine(source.FullName, "src", "index.ts"))) return source;
        }
        var packaged = new DirectoryInfo(Path.Combine(AppContext.BaseDirectory, "tools", name));
        return File.Exists(Path.Combine(packaged.FullName, "src", "index.ts")) ? packaged : null;
    }

    public static async Task<ProjectAnalyzerFingerprint?> ReadAsync(string analyzer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var root = ResolveRoot(analyzer);
        var tsx = root is null ? null : NodeIndexerProcessRunner.ResolveTsxCommand(root);
        if (root is null || tsx is null) return null; // Discovery never restores dependencies.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var process = new Process { StartInfo = new ProcessStartInfo(tsx)
        {
            WorkingDirectory = root.FullName, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
        } };
        process.StartInfo.ArgumentList.Add(Path.Combine(root.FullName, "src", "index.ts"));
        process.StartInfo.ArgumentList.Add("--describe-worker");
        try
        {
            process.Start();
            var output = ReadBoundedAsync(process.StandardOutput, timeout.Token);
            var errors = ReadBoundedAsync(process.StandardError, timeout.Token);
            await Task.WhenAll(output, errors, process.WaitForExitAsync(timeout.Token));
            return process.ExitCode == 0 ? Parse(analyzer, await output) : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return null; // Missing/malformed runtime identity is unknown, never a synthetic version.
        }
        finally
        {
            try { if (process.Id != 0 && !process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { /* Process could not be started. */ }
        }
    }

    internal static ProjectAnalyzerFingerprint? Parse(string analyzer, string output)
    {
        try
        {
            if (output.Length > 8192) return null;
            using var json = JsonDocument.Parse(output);
            var report = json.RootElement;
            if (report.GetProperty("contractVersion").GetString() != "1"
                || report.GetProperty("analyzer").GetString() != analyzer) return null;
            var fingerprint = report.GetProperty("fingerprint").Deserialize<ProjectAnalyzerFingerprint>(JsonOptions);
            return fingerprint?.Validate() is null ? fingerprint : null;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException) { return null; }
    }

    public static string Expected(ProjectAnalyzerFingerprint? fingerprint) => fingerprint is null
        ? "unknown" : JsonSerializer.Serialize(fingerprint, JsonOptions);

    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var buffer = new char[8193];
        var length = 0;
        while (length < buffer.Length)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(length), cancellationToken);
            if (read == 0) return new string(buffer, 0, length);
            length += read;
        }
        throw new InvalidDataException("Worker identity output exceeded its limit.");
    }
}
