using System.Diagnostics;

namespace CodeMeridian.Indexer.Cli.SessionEvaluation;

internal sealed class GitSessionChangeSource : ISessionChangeSource
{
    public async Task<SessionChangeSet> GetChangesAsync(DirectoryInfo root, string gitBase, CancellationToken cancellationToken)
    {
        var baseRef = string.IsNullOrWhiteSpace(gitBase) ? "HEAD" : gitBase.Trim();
        var output = await RunGitAsync(root, ["diff", "--name-status", "--find-renames=90%", "--diff-filter=ACMRTUXB", baseRef, "--"], cancellationToken);
        return ParseChangeSet(output);
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

    private static SessionChangeSet ParseChangeSet(string output)
    {
        var changedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var renamedFromByPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawLine in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = rawLine.Split('\t', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0)
                continue;

            var status = parts[0];
            if (status.StartsWith("R", StringComparison.OrdinalIgnoreCase)
                || status.StartsWith("C", StringComparison.OrdinalIgnoreCase))
            {
                if (parts.Length < 3)
                    continue;

                var originalPath = SessionPathNormalizer.Normalize(parts[1]);
                var changedPath = SessionPathNormalizer.Normalize(parts[2]);
                if (changedPath.Length == 0)
                    continue;

                changedFiles.Add(changedPath);
                if (originalPath.Length > 0)
                    renamedFromByPath[changedPath] = originalPath;

                continue;
            }

            if (parts.Length < 2)
                continue;

            var changedFile = SessionPathNormalizer.Normalize(parts[1]);
            if (changedFile.Length > 0)
                changedFiles.Add(changedFile);
        }

        return new SessionChangeSet(changedFiles, renamedFromByPath);
    }
}
