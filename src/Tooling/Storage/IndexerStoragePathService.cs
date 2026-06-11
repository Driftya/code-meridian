using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace CodeMeridian.Tooling.Storage;

public sealed class IndexerStoragePathService : IIndexerStoragePathService
{
    public DirectoryInfo ResolveCacheDirectory(DirectoryInfo root, string projectName, IndexerStorageMode storageMode)
    {
        if (storageMode == IndexerStorageMode.Repository)
            return new DirectoryInfo(Path.Combine(root.FullName, ".meridian", "cache"));

        var storageRoot = GetGlobalStorageRoot();
        var projectKey = ResolveProjectKey(root, projectName);
        return new DirectoryInfo(Path.Combine(storageRoot.FullName, "projects", projectKey, "cache"));
    }

    public string ResolveProjectKey(DirectoryInfo root, string projectName)
    {
        var normalizedProjectName = NormalizeSegment(projectName);
        var gitRemoteUrl = TryGetGitRemoteUrl(root);

        if (!string.IsNullOrWhiteSpace(normalizedProjectName))
        {
            var suffix = ShortHash(gitRemoteUrl ?? NormalizePath(root.FullName));
            return $"{normalizedProjectName}-{suffix}";
        }

        if (!string.IsNullOrWhiteSpace(gitRemoteUrl))
            return $"git-{ShortHash(gitRemoteUrl)}";

        var folderName = NormalizeSegment(root.Name);
        return $"{folderName}-{ShortHash(NormalizePath(root.FullName))}";
    }

    private static DirectoryInfo GetGlobalStorageRoot()
    {
        if (OperatingSystem.IsWindows())
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(localAppData))
                return new DirectoryInfo(Path.Combine(localAppData, "CodeMeridian"));
        }

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(profile))
            return new DirectoryInfo(Path.Combine(profile, ".codemeridian"));

        return new DirectoryInfo(Path.Combine(AppContext.BaseDirectory, ".codemeridian"));
    }

    private static string? TryGetGitRemoteUrl(DirectoryInfo root)
    {
        var gitRoot = FindGitRoot(root);
        if (gitRoot is null)
            return null;

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = gitRoot.FullName,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("config");
            startInfo.ArgumentList.Add("--get");
            startInfo.ArgumentList.Add("remote.origin.url");

            using var process = Process.Start(startInfo);
            if (process is null)
                return null;

            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(2000);
            return process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output) ? output : null;
        }
        catch
        {
            return null;
        }
    }

    private static DirectoryInfo? FindGitRoot(DirectoryInfo start)
    {
        for (var current = start; current is not null; current = current.Parent)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".git")) ||
                File.Exists(Path.Combine(current.FullName, ".git")))
            {
                return current;
            }
        }

        return null;
    }

    private static string NormalizeSegment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var builder = new StringBuilder(value.Length);
        var lastWasSeparator = false;

        foreach (var ch in value.Trim())
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
                lastWasSeparator = false;
            }
            else if (!lastWasSeparator)
            {
                builder.Append('-');
                lastWasSeparator = true;
            }
        }

        return builder.ToString().Trim('-');
    }

    private static string NormalizePath(string path) =>
        path.Replace('\\', '/').TrimEnd('/').ToLowerInvariant();

    private static string ShortHash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes)[..12].ToLowerInvariant();
    }
}
