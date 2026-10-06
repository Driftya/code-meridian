using System.Diagnostics;
using System.Text.Json;
using CodeMeridian.Core.CodeGraph;

namespace CodeMeridian.RoslynIndexer.Pipeline;

internal sealed record PackageProjectMetadata(
    string AssemblyName, string PackageId, string PackageVersion, IReadOnlyList<string> Frameworks,
    string? RepositoryUrl, string? Commit, bool Dirty)
{
    public static async Task<PackageProjectMetadata> ReadAsync(string projectPath, CancellationToken cancellationToken, string? framework = null)
    {
        var output = await RunAsync("dotnet", Path.GetDirectoryName(projectPath)!, cancellationToken,
            "msbuild", projectPath, "-nologo", "-getProperty:AssemblyName,PackageId,PackageVersion,TargetFramework,TargetFrameworks,RepositoryUrl",
            "-p:Configuration=Debug", framework is null ? "-p:Configuration=Debug" : "-p:TargetFramework=" + framework);
        using var json = JsonDocument.Parse(output);
        var properties = json.RootElement.GetProperty("Properties");
        string Read(string key) => properties.GetProperty(key).GetString() ?? "";
        var frameworks = (Read("TargetFrameworks") is { Length: > 0 } multiple ? multiple : Read("TargetFramework"))
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var directory = Path.GetDirectoryName(projectPath)!;
        var commit = await TryGitAsync(directory, cancellationToken, "rev-parse", "HEAD");
        var status = await TryGitAsync(directory, cancellationToken, "status", "--porcelain");
        var remote = Read("RepositoryUrl");
        if (string.IsNullOrWhiteSpace(remote)) remote = await TryGitAsync(directory, cancellationToken, "remote", "get-url", "origin") ?? "";
        return new(Read("AssemblyName"), Read("PackageId"), Read("PackageVersion"), frameworks,
            PackageRepositoryIdentity.Normalize(remote), commit?.Trim(), status is null || status.Length > 0);
    }

    private static async Task<string?> TryGitAsync(string directory, CancellationToken cancellationToken, params string[] args)
    {
        try { return await RunAsync("git", directory, cancellationToken, args); }
        catch (InvalidOperationException) { return null; }
    }

    internal static async Task<string> RunAsync(string executable, string directory, CancellationToken cancellationToken, params string[] args)
    {
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true
        };
        foreach (var argument in args) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start project metadata evaluation.");
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        try { await process.WaitForExitAsync(cancellationToken); }
        catch (OperationCanceledException) { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
        var output = await stdout;
        await stderr; // Consume without logging repo-controlled output or credentials.
        if (process.ExitCode != 0) throw new InvalidOperationException("Project metadata evaluation failed.");
        return output;
    }
}
