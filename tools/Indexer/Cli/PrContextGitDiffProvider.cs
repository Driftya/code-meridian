using System.Diagnostics;
using CodeMeridian.Indexer.Cli.SessionEvaluation;

namespace CodeMeridian.Indexer.Cli.Commands;

internal sealed class PrContextGitDiffProvider : IPrContextGitDiffProvider
{
    public async Task<IReadOnlyCollection<string>> GetChangedFilesAsync(
        DirectoryInfo root,
        string baseRef,
        string headRef,
        CancellationToken cancellationToken)
    {
        var normalizedBase = string.IsNullOrWhiteSpace(baseRef) ? "origin/main" : baseRef.Trim();
        var normalizedHead = string.IsNullOrWhiteSpace(headRef) ? "HEAD" : headRef.Trim();
        var range = $"{normalizedBase}...{normalizedHead}";

        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = root.FullName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        foreach (var argument in new[] { "diff", "--name-only", "--diff-filter=ACMRTUXB", range, "--" })
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start git.");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git diff failed: {error.Trim()}");

        return output
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(SessionPathNormalizer.Normalize)
            .Where(path => path.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
