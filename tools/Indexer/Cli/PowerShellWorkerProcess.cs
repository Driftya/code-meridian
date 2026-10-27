using System.Diagnostics;
using System.Text.Json;
using CodeMeridian.Core.Projects;

namespace CodeMeridian.Indexer.Cli.Commands;

internal static class PowerShellWorkerProcess
{
    public static string? ResolveExecutable()
    {
        var name = "CodeMeridian.PowerShellIndexer" + (OperatingSystem.IsWindows() ? ".exe" : "");
        var packaged = Path.Combine(AppContext.BaseDirectory, "tools", "PowerShellIndexer",
            System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier, name);
        if (File.Exists(packaged)) return packaged;
        // Development fallback is confined to the running CLI's checkout, never the analyzed repository.
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (!File.Exists(Path.Combine(directory.FullName, "tools", "Indexer", "CodeMeridian.Indexer.csproj"))) continue;
            foreach (var configuration in new[] { "Debug", "Release" })
            {
                var candidate = Path.Combine(directory.FullName, "tools", "PowerShellIndexer", "bin", configuration, "net10.0", name);
                if (File.Exists(candidate)) return candidate;
            }
            break;
        }
        return null;
    }

    public static async Task<ProjectAnalyzerFingerprint?> ReadFingerprintAsync(string analyzer, CancellationToken cancellationToken)
    {
        if (analyzer != "powershell") return await NodeWorkerIdentity.ReadAsync(analyzer, cancellationToken);
        try
        {
            var (exit, output) = await RunAsync(["--describe-worker"], 8192, cancellationToken);
            using var json = JsonDocument.Parse(output);
            return exit == 0 ? ParseFingerprint(json.RootElement) : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested) { return null; }
    }

    internal static ProjectAnalyzerFingerprint? ParseFingerprint(JsonElement worker)
    {
        if (worker.GetProperty("contractVersion").GetString() != "1.0"
            || worker.GetProperty("parseMode").GetString() != "module_analysis_syntax_only") return null;
        var fingerprint = new ProjectAnalyzerFingerprint(worker.GetProperty("implementationFingerprint").GetString()!,
            worker.GetProperty("parserFingerprint").GetString()!);
        return fingerprint.Validate() is null ? fingerprint : null;
    }

    internal static async Task<(int ExitCode, string Output)> RunAsync(IReadOnlyList<string> arguments, int maximumOutput,
        CancellationToken cancellationToken)
    {
        var executable = ResolveExecutable() ?? throw new InvalidOperationException("The isolated PowerShell worker is missing. Build or install the worker package; indexing never restores or builds it automatically.");
        // NuGet ZIP extraction does not preserve executable bits on nested worker apphosts.
        if (!OperatingSystem.IsWindows())
        {
            var mode = File.GetUnixFileMode(executable);
            if ((mode & UnixFileMode.UserExecute) == 0) File.SetUnixFileMode(executable, mode | UnixFileMode.UserExecute);
        }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        using var process = new Process { StartInfo = new(executable)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(executable)!
        } };
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        process.Start();
        try
        {
            var output = ReadBoundedAsync(process.StandardOutput, maximumOutput, timeout.Token);
            var errors = ReadBoundedAsync(process.StandardError, 8192, timeout.Token);
            // Observe any failed drain immediately; waiting for all tasks could leave a worker
            // blocked on its full output pipe until the process timeout.
            var pending = new List<Task> { output, errors, process.WaitForExitAsync(timeout.Token) };
            while (pending.Count > 0)
            {
                var finished = await Task.WhenAny(pending);
                await finished;
                pending.Remove(finished);
            }
            return (process.ExitCode, await output);
        }
        finally
        {
            timeout.Cancel();
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, int maximum, CancellationToken cancellationToken)
    {
        var result = new System.Text.StringBuilder();
        var buffer = new char[8192];
        while (true)
        {
            var count = await reader.ReadAsync(buffer, cancellationToken);
            if (count == 0) return result.ToString();
            if (result.Length + count > maximum) throw new InvalidDataException("PowerShell worker output exceeded the protocol budget.");
            result.Append(buffer, 0, count);
        }
    }
}
