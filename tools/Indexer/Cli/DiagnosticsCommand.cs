using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using CodeMeridian.Sdk;
using CodeMeridian.Tooling.Discovery;

namespace CodeMeridian.Indexer.Cli.Commands;

internal sealed class DiagnosticsCommand(IProjectDiscoveryService projectDiscoveryService)
{
    public async Task<int> RunAsync(
        DirectoryInfo rootPath,
        IReadOnlyCollection<DirectoryInfo> typeScriptRoots,
        string project,
        string codeMeridianUrl,
        string? apiKey,
        bool allowRepoScripts)
    {
        Console.WriteLine();
        Console.WriteLine("Indexing diagnostics...");

        using var httpClient = new HttpClient
        {
            BaseAddress = new Uri(codeMeridianUrl),
            Timeout = TimeSpan.FromMinutes(10)
        };
        httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
        if (!string.IsNullOrWhiteSpace(apiKey))
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var client = new CodeMeridianClient(httpClient);
        await client.ClearProjectDiagnosticsAsync(project);

        var findings = new List<DiagnosticFinding>();

        if (allowRepoScripts && projectDiscoveryService.ContainsFile(rootPath, ".cs"))
        {
            var build = await RunCaptureAsync("dotnet", BuildDotnetBuildArguments(rootPath), rootPath);
            var dotnetFindings = ParseDotnetDiagnostics(build.Output, rootPath, project);
            findings.AddRange(dotnetFindings);
            Console.WriteLine($"  dotnet build exit code {build.ExitCode}; parsed {dotnetFindings.Count} diagnostics.");
        }
        else if (projectDiscoveryService.ContainsFile(rootPath, ".cs"))
        {
            Console.WriteLine("  C# build diagnostics skipped. Use --allow-repo-scripts to run repo-controlled build steps.");
        }

        foreach (var typeScriptRoot in typeScriptRoots.Where(root => File.Exists(Path.Combine(root.FullName, "tsconfig.json"))))
        {
            var tsc = ResolveLocalNodeBinary(typeScriptRoot, "tsc");
            if (tsc is not null)
            {
                var result = await RunCaptureAsync(
                    tsc,
                    ["--noEmit", "--pretty", "false", "--noUnusedLocals", "--noUnusedParameters"],
                    typeScriptRoot);
                var typeScriptFindings = ParseTypeScriptDiagnostics(result.Output, rootPath, typeScriptRoot, project);
                findings.AddRange(typeScriptFindings);
                Console.WriteLine($"  tsc {Path.GetRelativePath(rootPath.FullName, typeScriptRoot.FullName)} exit code {result.ExitCode}; parsed {typeScriptFindings.Count} diagnostics.");
            }
            else
            {
                Console.WriteLine($"  TypeScript diagnostics unavailable in {Path.GetRelativePath(rootPath.FullName, typeScriptRoot.FullName)}: local tsc not found.");
            }
        }

        if (allowRepoScripts)
        {
            var lintCommand = ResolveLintCommand(rootPath);
            if (lintCommand is not null)
            {
                var result = await RunCaptureAsync(lintCommand.Value.FileName, lintCommand.Value.Arguments, rootPath);
                var lintFindings = ParseLintDiagnostics(result.Output, rootPath, project);
                findings.AddRange(lintFindings);
                Console.WriteLine($"  lint exit code {result.ExitCode}; parsed {lintFindings.Count} diagnostics.");
            }
        }
        else if (ResolveLintCommand(rootPath) is not null)
        {
            Console.WriteLine("  Lint diagnostics skipped. Use --allow-repo-scripts to run repo-controlled lint steps.");
        }

        var distinct = findings
            .GroupBy(f => f.Id, StringComparer.Ordinal)
            .Select(g => g.First())
            .ToArray();

        foreach (var finding in distinct)
        {
            await client.IngestCodeNodeAsync(
                finding.Id,
                $"{finding.Severity} {finding.Code}",
                "Diagnostic",
                namespacePath: finding.Source,
                filePath: finding.FilePath,
                lineNumber: finding.Line,
                summary: finding.Message,
                projectContext: project);

            if (!string.IsNullOrWhiteSpace(finding.FilePath))
            {
                await client.IngestRelationshipAsync(
                    $"{project}::File::{finding.FilePath}",
                    finding.Id,
                    "Contains");
            }
        }

        Console.WriteLine($"  Indexed {distinct.Length} diagnostics.");
        return 0;
    }

    private static async Task<(int ExitCode, string Output)> RunCaptureAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        DirectoryInfo workingDirectory)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory.FullName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        process.Start();
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, stdout + Environment.NewLine + stderr);
    }

    internal static string[] BuildDotnetBuildArguments(DirectoryInfo rootPath)
    {
        var scratchRoot = Path.Combine(
            Path.GetTempPath(),
            "codemeridian-diagnostics",
            Hash(rootPath.FullName));

        var outputRoot = Path.Combine(scratchRoot, "bin") + Path.DirectorySeparatorChar;

        Directory.CreateDirectory(outputRoot);

        return
        [
            "build",
            "--no-restore",
            "--nologo",
            "-p:BaseOutputPath=" + outputRoot
        ];
    }

    private static IReadOnlyList<DiagnosticFinding> ParseDotnetDiagnostics(
        string output,
        DirectoryInfo rootPath,
        string project)
    {
        const string pattern = @"^(?<file>.+?)\((?<line>\d+),(?<column>\d+)\):\s(?<severity>warning|error)\s(?<code>[A-Z][A-Z0-9]*\d+[A-Z0-9]*):\s(?<message>.+?)(?:\s\[(?<project>.+?)\])?$";
        return ParseDiagnostics(output, rootPath, rootPath, project, "dotnet", pattern);
    }

    private static IReadOnlyList<DiagnosticFinding> ParseTypeScriptDiagnostics(
        string output,
        DirectoryInfo rootPath,
        DirectoryInfo workingDirectory,
        string project)
    {
        const string pattern = @"^(?<file>.+?)\((?<line>\d+),(?<column>\d+)\):\s(?<severity>error)\s(?<code>TS\d+):\s(?<message>.+)$";
        return ParseDiagnostics(output, rootPath, workingDirectory, project, "tsc", pattern);
    }

    private static IReadOnlyList<DiagnosticFinding> ParseLintDiagnostics(
        string output,
        DirectoryInfo rootPath,
        string project)
    {
        const string pattern = @"^\s*(?<line>\d+):(?<column>\d+)\s+(?<severity>error|warning|warn)\s+(?<message>.+?)\s+(?<code>[@\w/-]+)$";
        var findings = new List<DiagnosticFinding>();
        string? currentFile = null;

        foreach (var rawLine in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.TrimEnd();
            if (!line.StartsWith(' ') && LooksLikePath(line))
            {
                currentFile = NormalizePath(line, rootPath, rootPath);
                continue;
            }

            if (currentFile is null)
                continue;

            var match = Regex.Match(line, pattern, RegexOptions.IgnoreCase);
            if (!match.Success)
                continue;

            findings.Add(CreateDiagnostic(
                project,
                "eslint",
                match.Groups["severity"].Value is "warn" ? "warning" : match.Groups["severity"].Value,
                match.Groups["code"].Value,
                match.Groups["message"].Value.Trim(),
                currentFile,
                ParseInt(match.Groups["line"].Value),
                ParseInt(match.Groups["column"].Value)));
        }

        return findings;
    }

    private static IReadOnlyList<DiagnosticFinding> ParseDiagnostics(
        string output,
        DirectoryInfo rootPath,
        DirectoryInfo workingDirectory,
        string project,
        string source,
        string pattern)
    {
        var findings = new List<DiagnosticFinding>();

        foreach (var rawLine in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var match = Regex.Match(rawLine.TrimEnd(), pattern, RegexOptions.IgnoreCase);
            if (!match.Success)
                continue;

            findings.Add(CreateDiagnostic(
                project,
                source,
                match.Groups["severity"].Value,
                match.Groups["code"].Value,
                match.Groups["message"].Value.Trim(),
                NormalizePath(match.Groups["file"].Value, rootPath, workingDirectory),
                ParseInt(match.Groups["line"].Value),
                ParseInt(match.Groups["column"].Value)));
        }

        return findings;
    }

    private static DiagnosticFinding CreateDiagnostic(
        string project,
        string source,
        string severity,
        string code,
        string message,
        string filePath,
        int? line,
        int? column)
    {
        var normalizedSeverity = severity.Equals("warn", StringComparison.OrdinalIgnoreCase)
            ? "warning"
            : severity.ToLowerInvariant();
        var hashInput = $"{project}|{source}|{normalizedSeverity}|{code}|{filePath}|{line}|{column}|{message}";
        var id = $"{project}::Diagnostic::{Hash(hashInput)}";
        return new DiagnosticFinding(id, normalizedSeverity, code, message, filePath, line, column, source);
    }

    private static string? ResolveLocalNodeBinary(DirectoryInfo rootPath, string name)
    {
        var executable = OperatingSystem.IsWindows() ? $"{name}.cmd" : name;
        for (var current = rootPath; current is not null; current = current.Parent)
        {
            var candidate = Path.Combine(current.FullName, "node_modules", ".bin", executable);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static (string FileName, string[] Arguments)? ResolveLintCommand(DirectoryInfo rootPath)
    {
        var packageJson = new FileInfo(Path.Combine(rootPath.FullName, "package.json"));
        if (packageJson.Exists)
        {
            var content = File.ReadAllText(packageJson.FullName);
            if (content.Contains("\"lint\"", StringComparison.OrdinalIgnoreCase))
                return (NpmCommand(), ["run", "lint"]);
        }

        var eslint = ResolveLocalNodeBinary(rootPath, "eslint");
        return eslint is null ? null : (eslint, ["."]);
    }

    private static string NormalizePath(string path, DirectoryInfo rootPath, DirectoryInfo workingDirectory)
    {
        var trimmed = path.Trim().Trim('"');
        var fullPath = Path.IsPathRooted(trimmed)
            ? trimmed
            : Path.GetFullPath(trimmed, workingDirectory.FullName);

        return Path.GetRelativePath(rootPath.FullName, fullPath).Replace('\\', '/');
    }

    private static bool LooksLikePath(string value) =>
        value.Contains('/') || value.Contains('\\') || value.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) ||
        value.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase) || value.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ||
        value.EndsWith(".jsx", StringComparison.OrdinalIgnoreCase);

    private static int? ParseInt(string value) =>
        int.TryParse(value, out var parsed) ? parsed : null;

    private static string Hash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }

    private static string NpmCommand() => ExternalCommandResolver.NpmCommand();

    private sealed record DiagnosticFinding(
        string Id,
        string Severity,
        string Code,
        string Message,
        string FilePath,
        int? Line,
        int? Column,
        string Source);
}
