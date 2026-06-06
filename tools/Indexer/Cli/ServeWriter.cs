using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CodeMeridian.Indexer.Cli;

internal sealed class ServeWriter
{
    private const string AuthEnvVar = "CodeMeridian_Auth_ApiKey";

    public ServeApplyResult Apply(ServeOptions options)
    {
        Directory.CreateDirectory(options.RootDirectory.FullName);

        var changes = new List<ServeFileChange>
        {
            WriteEnv(options),
            WriteCompose(options),
            WriteMcpJson(options),
            WriteCodexToml(options),
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
        ];
    }

    private static ServeFileChange WriteEnv(ServeOptions options)
    {
        var path = Path.Combine(options.RootDirectory.FullName, ".env");
        var defaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["CODEMERIDIAN_PORT"] = options.Port.ToString(),
            ["CodeMeridian_Url"] = $"http://{options.Host}:{options.Port}",
            ["NEO4J_HTTP_PORT"] = options.Neo4jHttpPort.ToString(),
            ["NEO4J_BOLT_PORT"] = options.Neo4jBoltPort.ToString(),
            ["NEO4J_PASSWORD"] = "CodeMeridian",
            [AuthEnvVar] = GenerateToken(),
            ["Embedding__Enabled"] = "false",
            ["Embedding__Provider"] = "Ollama",
        };

        if (!File.Exists(path))
        {
            var content = string.Join(Environment.NewLine, defaults.Select(pair => $"{pair.Key}={pair.Value}")) + Environment.NewLine;
            File.WriteAllText(path, content);
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
        var server = new JsonObject
        {
            ["type"] = "sse",
            ["url"] = mcpUrl,
            ["headers"] = new JsonObject
            {
                ["Authorization"] = $"Bearer ${{env:{AuthEnvVar}}}"
            }
        };

        JsonObject root;
        var existed = File.Exists(path);
        if (existed)
        {
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
        }
        else
        {
            root = [];
        }

        if (root["servers"] is not JsonObject servers)
        {
            servers = [];
            root["servers"] = servers;
        }

        servers["CodeMeridian"] = server;
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
        var section = $"""
            [mcp_servers.CodeMeridian]
            url = "{mcpUrl}"
            default_tools_approval_mode = "auto"
            startup_timeout_sec = 15
            tool_timeout_sec = 60
            bearer_token_env_var = "{AuthEnvVar}"
            """;

        if (!File.Exists(path))
        {
            File.WriteAllText(path, section + Environment.NewLine);
            return new ServeFileChange(path, "created");
        }

        var original = File.ReadAllText(path);
        var content = ReplaceTomlSection(original, "mcp_servers.CodeMeridian", section);
        return WriteMergedFile(path, content, existed: true, force);
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

    private static string BuildCompose(ServeOptions options) =>
        $$"""
        services:
          neo4j:
            image: neo4j:5.20
            container_name: codemeridian-neo4j
            ports:
              - "${NEO4J_HTTP_PORT:-{{options.Neo4jHttpPort}}}:7474"
              - "${NEO4J_BOLT_PORT:-{{options.Neo4jBoltPort}}}:7687"
            environment:
              NEO4J_AUTH: neo4j/${NEO4J_PASSWORD:-CodeMeridian}
              NEO4J_PLUGINS: '["apoc", "graph-data-science"]'
              NEO4J_dbms_memory_heap_initial__size: 512m
              NEO4J_dbms_memory_heap_max__size: 2G
              NEO4J_server_default__listen__address: 0.0.0.0
            volumes:
              - codemeridian_neo4j_data:/data
              - codemeridian_neo4j_logs:/logs
            healthcheck:
              test: ["CMD-SHELL", "wget -O /dev/null -q http://localhost:7474 || exit 1"]
              interval: 30s
              timeout: 10s
              retries: 5
              start_period: 40s

          codemeridian-mcp:
            image: {{options.Image}}
            container_name: codemeridian-mcp
            extra_hosts:
              - "host.docker.internal:host-gateway"
            ports:
              - "${CODEMERIDIAN_PORT:-{{options.Port}}}:8080"
            environment:
              Neo4j__Uri: bolt://neo4j:7687
              Neo4j__Username: neo4j
              Neo4j__Password: ${NEO4J_PASSWORD:-CodeMeridian}
              Neo4j__EmbeddingDimensions: ${Neo4j__EmbeddingDimensions:-1536}
              CodeMeridian_Auth_ApiKey: ${CodeMeridian_Auth_ApiKey:-}
              Embedding__Enabled: ${Embedding__Enabled:-false}
              Embedding__Provider: ${Embedding__Provider:-Ollama}
              Embedding__OllamaBaseUrl: ${Embedding__OllamaBaseUrl:-http://host.docker.internal:11434}
              Embedding__OllamaModel: ${Embedding__OllamaModel:-nomic-embed-text}
              Embedding__OpenAiApiKey: ${Embedding__OpenAiApiKey:-}
              Embedding__OpenAiModel: ${Embedding__OpenAiModel:-text-embedding-3-small}
              Embedding__BatchSize: ${Embedding__BatchSize:-50}
            depends_on:
              neo4j:
                condition: service_healthy

        volumes:
          codemeridian_neo4j_data:
          codemeridian_neo4j_logs:
        """;
}

internal sealed record ServeApplyResult(string ComposePath, IReadOnlyList<ServeFileChange> Changes);

internal sealed record ServeFileChange(string Path, string Status);
