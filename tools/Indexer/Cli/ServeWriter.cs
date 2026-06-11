using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CodeMeridian.Indexer.Cli;

internal sealed class ServeWriter
{
    private const string AuthEnvVar = "CodeMeridian_Auth_ApiKey";
    private const string EnvSampleFileName = ".env.sample";
    private const string ComposeSampleFileName = "docker-compose.sample.yaml";

    public ServeApplyResult Apply(ServeOptions options)
    {
        Directory.CreateDirectory(options.RootDirectory.FullName);

        var changes = new List<ServeFileChange>
        {
            WriteEnv(options),
            WriteCompose(options),
            WriteMcpJson(options),
            WriteCodexToml(options),
            WriteContinueMcpServer(options),
        };

        var composePath = Path.Combine(options.RootDirectory.FullName, options.ComposeFileName);
        return new ServeApplyResult(composePath, changes);
    }

    public IReadOnlyList<ServeFileChange> ApplyClientConfig(DirectoryInfo rootDirectory, string codeMeridianUrl, bool force)
    {
        Directory.CreateDirectory(rootDirectory.FullName);
        var mcpUrl = BuildMcpUrl(codeMeridianUrl);

        return
        [
            WriteMcpJson(rootDirectory, mcpUrl, force),
            WriteCodexToml(rootDirectory, mcpUrl, force),
            WriteContinueMcpServer(rootDirectory, mcpUrl, force),
        ];
    }

    private static ServeFileChange WriteEnv(ServeOptions options)
    {
        var path = Path.Combine(options.RootDirectory.FullName, ".env");
        var defaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["CODEMERIDIAN_PORT"] = options.Port.ToString(),
            ["CodeMeridian_Url"] = $"http://{options.Host}:{options.Port}",
            ["CodeMeridian_Project"] = string.Empty,
            ["NEO4J_HTTP_PORT"] = options.Neo4jHttpPort.ToString(),
            ["NEO4J_BOLT_PORT"] = options.Neo4jBoltPort.ToString(),
            ["NEO4J_PASSWORD"] = "CodeMeridian",
            [AuthEnvVar] = GenerateToken(),
            ["Embedding__Enabled"] = "false",
            ["Embedding__Provider"] = "Ollama",
        };

        if (!File.Exists(path))
        {
            File.WriteAllText(path, BuildEnvContent(options, defaults));
            return new ServeFileChange(path, "created");
        }

        var original = File.ReadAllText(path);
        var lines = original.Split(["\r\n", "\n"], StringSplitOptions.None).ToList();
        var existingKeys = ReadEnvKeys(lines);

        if (options.Force)
        {
            Backup(path);
            var rewritten = RewriteEnv(lines, defaults);
            File.WriteAllText(path, rewritten);
            return new ServeFileChange(path, "overwritten");
        }

        var missing = defaults
            .Where(pair => !existingKeys.Contains(pair.Key))
            .ToArray();

        if (missing.Length == 0)
            return new ServeFileChange(path, "skipped");

        if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[^1]))
            lines.Add(string.Empty);

        foreach (var pair in missing)
            lines.Add($"{pair.Key}={pair.Value}");

        File.WriteAllText(path, NormalizeLines(lines));
        return new ServeFileChange(path, "merged");
    }

    private static ServeFileChange WriteCompose(ServeOptions options)
    {
        var path = Path.Combine(options.RootDirectory.FullName, options.ComposeFileName);
        var content = BuildCompose(options);
        return WriteWholeFile(path, content, options.Force);
    }

    private static ServeFileChange WriteMcpJson(ServeOptions options)
    {
        return WriteMcpJson(options.RootDirectory, $"http://{options.Host}:{options.Port}/sse", options.Force);
    }

    private static ServeFileChange WriteMcpJson(DirectoryInfo rootDirectory, string mcpUrl, bool force)
    {
        var directory = Path.Combine(rootDirectory.FullName, ".vscode");
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, "mcp.json");
        if (!File.Exists(path))
        {
            File.WriteAllText(path, BuildMcpJsonContent(mcpUrl));
            return new ServeFileChange(path, "created");
        }

        JsonObject root;
        var existed = true;
        try
        {
            root = JsonNode.Parse(
                File.ReadAllText(path),
                new JsonNodeOptions { PropertyNameCaseInsensitive = false },
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip,
                })?.AsObject() ?? [];
        }
        catch when (force)
        {
            Backup(path);
            root = [];
            existed = false;
        }

        if (root["servers"] is not JsonObject servers)
        {
            servers = [];
            root["servers"] = servers;
        }

        servers["CodeMeridian"] = BuildMcpServerNode(mcpUrl);
        var content = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine;

        return WriteMergedFile(path, content, existed, force);
    }

    private static ServeFileChange WriteCodexToml(ServeOptions options)
    {
        return WriteCodexToml(options.RootDirectory, $"http://{options.Host}:{options.Port}/sse", options.Force);
    }

    private static ServeFileChange WriteCodexToml(DirectoryInfo rootDirectory, string mcpUrl, bool force)
    {
        var directory = Path.Combine(rootDirectory.FullName, ".codex");
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, "config.toml");
        var section = ExtractTomlSection(BuildCodexConfigContent(mcpUrl), "mcp_servers.CodeMeridian");

        if (!File.Exists(path))
        {
            File.WriteAllText(path, BuildCodexConfigContent(mcpUrl));
            return new ServeFileChange(path, "created");
        }

        var original = File.ReadAllText(path);
        var content = ReplaceTomlSection(original, "mcp_servers.CodeMeridian", section);
        return WriteMergedFile(path, content, existed: true, force);
    }

    private static ServeFileChange WriteContinueMcpServer(ServeOptions options) =>
        WriteContinueMcpServer(options.RootDirectory, $"http://{options.Host}:{options.Port}/sse", options.Force);

    private static ServeFileChange WriteContinueMcpServer(DirectoryInfo rootDirectory, string mcpUrl, bool force)
    {
        var directory = Path.Combine(rootDirectory.FullName, ".continue", "mcpServers");
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, "code-meridian.yaml");
        var content = BuildContinueMcpServerContent(mcpUrl);
        return WriteWholeFile(path, content, force);
    }

    private static ServeFileChange WriteWholeFile(string path, string content, bool force)
    {
        if (!File.Exists(path))
        {
            File.WriteAllText(path, content);
            return new ServeFileChange(path, "created");
        }

        if (File.ReadAllText(path) == content)
            return new ServeFileChange(path, "skipped");

        if (!force)
            return new ServeFileChange(path, "skipped");

        Backup(path);
        File.WriteAllText(path, content);
        return new ServeFileChange(path, "overwritten");
    }

    private static ServeFileChange WriteMergedFile(string path, string content, bool existed, bool force)
    {
        if (!existed || !File.Exists(path))
        {
            File.WriteAllText(path, content);
            return new ServeFileChange(path, "created");
        }

        if (File.ReadAllText(path) == content)
            return new ServeFileChange(path, "skipped");

        if (force)
            Backup(path);

        File.WriteAllText(path, content);
        return new ServeFileChange(path, force ? "overwritten" : "merged");
    }

    private static HashSet<string> ReadEnvKeys(IEnumerable<string> lines)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                continue;

            var separator = trimmed.IndexOf('=');
            if (separator > 0)
                keys.Add(trimmed[..separator].Trim());
        }

        return keys;
    }

    private static string RewriteEnv(IReadOnlyList<string> lines, IReadOnlyDictionary<string, string> defaults)
    {
        var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rewritten = new List<string>();

        foreach (var line in lines)
        {
            var separator = line.IndexOf('=');
            if (separator <= 0 || line.TrimStart().StartsWith('#'))
            {
                rewritten.Add(line);
                continue;
            }

            var key = line[..separator].Trim();
            if (defaults.TryGetValue(key, out var value))
            {
                rewritten.Add($"{key}={value}");
                emitted.Add(key);
            }
            else
            {
                rewritten.Add(line);
            }
        }

        foreach (var pair in defaults)
        {
            if (!emitted.Contains(pair.Key))
                rewritten.Add($"{pair.Key}={pair.Value}");
        }

        return NormalizeLines(rewritten);
    }

    private static string ReplaceTomlSection(string original, string sectionName, string replacement)
    {
        var lines = original.Split(["\r\n", "\n"], StringSplitOptions.None).ToList();
        var start = -1;
        var end = lines.Count;

        for (var i = 0; i < lines.Count; i++)
        {
            if (!IsTomlSection(lines[i], sectionName))
                continue;

            start = i;
            for (var j = i + 1; j < lines.Count; j++)
            {
                if (IsAnyTomlSection(lines[j]))
                {
                    end = j;
                    break;
                }
            }

            break;
        }

        var replacementLines = replacement.Split(["\r\n", "\n"], StringSplitOptions.None);
        if (start >= 0)
        {
            lines.RemoveRange(start, end - start);
            lines.InsertRange(start, replacementLines);
        }
        else
        {
            if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[^1]))
                lines.Add(string.Empty);
            lines.AddRange(replacementLines);
        }

        return NormalizeLines(lines);
    }

    private static bool IsTomlSection(string line, string sectionName) =>
        line.Trim().Equals($"[{sectionName}]", StringComparison.Ordinal);

    private static bool IsAnyTomlSection(string line)
    {
        var trimmed = line.Trim();
        return trimmed.StartsWith('[') && trimmed.EndsWith(']');
    }

    private static string NormalizeLines(IEnumerable<string> lines) =>
        string.Join(Environment.NewLine, TrimTrailingEmptyLines(lines)) + Environment.NewLine;

    private static IReadOnlyList<string> TrimTrailingEmptyLines(IEnumerable<string> lines)
    {
        var result = lines.ToList();
        while (result.Count > 0 && result[^1].Length == 0)
            result.RemoveAt(result.Count - 1);
        return result;
    }

    private static void Backup(string path)
    {
        var backupPath = $"{path}.{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.bak";
        File.Copy(path, backupPath, overwrite: false);
    }

    private static string GenerateToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string BuildMcpUrl(string codeMeridianUrl)
    {
        var trimmed = codeMeridianUrl.TrimEnd('/');
        return trimmed.EndsWith("/sse", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"{trimmed}/sse";
    }

    private static string BuildEnvContent(ServeOptions options, IReadOnlyDictionary<string, string> defaults)
    {
        return RenderServeTemplate(
            ReadRequiredTemplate(EnvSampleFileName),
            options,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["apiKey"] = defaults[AuthEnvVar]
            });
    }

    private static string BuildMcpJsonContent(string mcpUrl)
    {
        return RenderTemplate(
            ReadRequiredTemplate(Path.Combine(".vscode", "mcp.sample.json")),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["mcpUrl"] = mcpUrl
            });
    }

    private static string BuildCodexConfigContent(string mcpUrl)
    {
        return RenderTemplate(
            ReadRequiredTemplate(Path.Combine(".codex", "config.sample.toml")),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["mcpUrl"] = mcpUrl
            });
    }

    private static string BuildContinueMcpServerContent(string mcpUrl)
    {
        return RenderTemplate(
            ReadRequiredTemplate(Path.Combine(".continue", "mcpServers", "code-meridian.sample.yaml")),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["mcpUrl"] = mcpUrl
            });
    }

    private static string BuildCompose(ServeOptions options)
    {
        return RenderServeTemplate(ReadRequiredTemplate(ComposeSampleFileName), options);
    }

    private static string RenderServeTemplate(
        string template,
        ServeOptions options,
        IReadOnlyDictionary<string, string>? extraValues = null)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["host"] = options.Host,
            ["port"] = options.Port.ToString(),
            ["neo4jHttpPort"] = options.Neo4jHttpPort.ToString(),
            ["neo4jBoltPort"] = options.Neo4jBoltPort.ToString(),
            ["image"] = options.Image
        };

        if (extraValues is not null)
        {
            foreach (var pair in extraValues)
                values[pair.Key] = pair.Value;
        }

        return RenderTemplate(template, values);
    }

    private static string RenderTemplate(string template, IReadOnlyDictionary<string, string> values)
    {
        var rendered = values.Aggregate(
            template,
            (current, pair) => current.Replace($"{{{{{pair.Key}}}}}", pair.Value, StringComparison.Ordinal));

        return rendered.TrimEnd() + Environment.NewLine;
    }

    private static JsonNode BuildMcpServerNode(string mcpUrl)
    {
        var root = JsonNode.Parse(
            BuildMcpJsonContent(mcpUrl),
            new JsonNodeOptions { PropertyNameCaseInsensitive = false },
            new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            })?.AsObject();

        if (root?["servers"] is JsonObject servers && servers["CodeMeridian"] is JsonNode server)
            return server.DeepClone();

        throw new InvalidOperationException("Template .vscode/mcp.sample.json must define servers.CodeMeridian.");
    }

    private static string ExtractTomlSection(string content, string sectionName)
    {
        var lines = content.Split(["\r\n", "\n"], StringSplitOptions.None).ToList();
        var start = lines.FindIndex(line => IsTomlSection(line, sectionName));
        if (start < 0)
            throw new InvalidOperationException($"Template .codex/config.sample.toml must define [{sectionName}].");

        var end = lines.Count;
        for (var i = start + 1; i < lines.Count; i++)
        {
            if (IsAnyTomlSection(lines[i]))
            {
                end = i;
                break;
            }
        }

        return string.Join(Environment.NewLine, lines.Skip(start).Take(end - start)).TrimEnd();
    }

    private static string ReadRequiredTemplate(string fileName)
    {
        var sourcePath = Path.Combine(AppContext.BaseDirectory, fileName);
        if (File.Exists(sourcePath))
            return File.ReadAllText(sourcePath);

        throw new InvalidOperationException($"Required template file is missing: {sourcePath}");
    }
}

internal sealed record ServeApplyResult(string ComposePath, IReadOnlyList<ServeFileChange> Changes);

internal sealed record ServeFileChange(string Path, string Status);
