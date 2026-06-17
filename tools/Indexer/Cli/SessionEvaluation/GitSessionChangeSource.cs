using System.Diagnostics;

namespace CodeMeridian.Indexer.Cli.SessionEvaluation;

internal sealed class GitSessionChangeSource : ISessionChangeSource
{
    public async Task<SessionChangeSet> GetChangesAsync(DirectoryInfo root, string gitBase, CancellationToken cancellationToken)
    {
        var baseRef = string.IsNullOrWhiteSpace(gitBase) ? "HEAD" : gitBase.Trim();
        var output = await RunGitAsync(root, ["diff", "--name-only", "--diff-filter=ACMRTUXB", baseRef, "--"], cancellationToken);
        return new SessionChangeSet(ParsePaths(output));
    }

    private static async Task<string> RunGitAsync(DirectoryInfo root, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = root.FullName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start git.");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        var output = await outputTask;
        var error = await errorTask;

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git diff failed: {error.Trim()}");

        return output;
    }

    private static IReadOnlySet<string> ParsePaths(string output) =>
        output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(SessionPathNormalizer.Normalize)
            .Where(path => path.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
