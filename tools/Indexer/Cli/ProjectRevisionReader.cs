using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using CodeMeridian.Core.Projects;

namespace CodeMeridian.Indexer.Cli.Commands;

internal static class ProjectRevisionReader
{
    public static ProjectRepositoryRevision? Read(DirectoryInfo root, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var commit = RunGit(root, ["rev-parse", "--verify", "HEAD"], cancellationToken)?.Trim();
        if (commit is null || new ProjectRepositoryRevision(commit, null).Validate() is not null) return null;
        var status = RunGit(root, ["status", "--porcelain=v1", "--untracked-files=normal", "--ignore-submodules=all"], cancellationToken);
        var after = RunGit(root, ["rev-parse", "--verify", "HEAD"], cancellationToken)?.Trim();
        return after == commit ? new(commit, status is null ? null : status.Length > 0) : null;
    }

    private static string? RunGit(DirectoryInfo root, string[] arguments, CancellationToken cancellationToken)
        => RunGitAsync(root, arguments, cancellationToken).GetAwaiter().GetResult();

    private static async Task<string?> RunGitAsync(DirectoryInfo root, string[] arguments, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = root.FullName, RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true
        };
        // Ignore inherited Git repository/config overrides and disable optional writes and fsmonitor hooks.
        foreach (var key in start.Environment.Keys.Where(key => key.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase)).ToArray())
            start.Environment.Remove(key);
        foreach (var argument in new[] { "--no-optional-locks", "-c", "core.fsmonitor=false", "-c", "core.untrackedCache=false" }.Concat(arguments))
            start.ArgumentList.Add(argument);
        try
        {
            using var process = Process.Start(start);
            if (process is null) return null;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            var output = ReadBoundedAsync(process.StandardOutput, timeout.Token);
            var error = ReadBoundedAsync(process.StandardError, timeout.Token);
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                await Task.WhenAll(output, error).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                return process.ExitCode == 0 ? await output.ConfigureAwait(false) : null;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return null; }
            finally
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { } // The process may have exited between the check and the kill.
                try { await Task.WhenAll(output, error).ConfigureAwait(false); }
                catch (Exception ex) when (ex is IOException or OperationCanceledException) { }
            }
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return null; // Missing Git or unavailable metadata does not make file enumeration partial.
        }
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var text = new StringBuilder();
        var buffer = new char[1024];
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
            if (text.Length < 256) text.Append(buffer, 0, Math.Min(count, 256 - text.Length));
        return text.ToString();
    }
}
