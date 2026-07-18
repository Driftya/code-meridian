using CodeMeridian.Indexer.Cli;
using FluentAssertions;

namespace CodeMeridian.Indexer.Tests.Cli;

public sealed class ServeWriterTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "codemeridian-serve-writer-tests",
        Guid.NewGuid().ToString("N"));

    public ServeWriterTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void Apply_CreatesOnlyRuntimeFilesInEmptyDirectory()
    {
        var result = Apply();

        File.Exists(Path.Combine(_root, ".env")).Should().BeTrue();
        File.Exists(Path.Combine(_root, "docker-compose.codemeridian.yml")).Should().BeTrue();
        File.Exists(Path.Combine(_root, ".vscode", "mcp.json")).Should().BeFalse();
        File.Exists(Path.Combine(_root, ".codex", "config.toml")).Should().BeFalse();
        File.Exists(Path.Combine(_root, ".continue", "mcpServers", "code-meridian.yaml")).Should().BeFalse();
        result.Changes.Should().OnlyContain(change => change.Status == "created");
    }

    [Fact]
    public void Apply_GeneratesComposeWithPublishedImage()
    {
        Apply(image: "ghcr.io/example/codemeridian-mcp:test");

        var compose = File.ReadAllText(Path.Combine(_root, "docker-compose.codemeridian.yml"));

        compose.Should().Contain("image: ghcr.io/example/codemeridian-mcp:test");
        compose.Should().NotContain("build:");
        compose.Should().Contain("${CODEMERIDIAN_PORT:-5100}:8080");
    }

    [Fact]
    public void ApplyClientConfig_MergesMcpJsonAndPreservesUnrelatedServer()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".vscode"));
        File.WriteAllText(
            Path.Combine(_root, ".vscode", "mcp.json"),
            """
            {
              // user-owned server
              "servers": {
                "Other": {
                  "type": "stdio",
                  "command": "other"
                }
              }
            }
            """);

        ApplyClientConfig();

        var json = File.ReadAllText(Path.Combine(_root, ".vscode", "mcp.json"));

        json.Should().Contain("\"Other\"");
        json.Should().Contain("\"CodeMeridian\"");
        json.Should().Contain("\"url\": \"http://localhost:5100/sse\"");
        json.Should().Contain("Bearer ${env:CodeMeridian_Auth_ApiKey}");
    }

    [Fact]
    public void ApplyClientConfig_MergesCodexConfigAndPreservesUnrelatedSections()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".codex"));
        File.WriteAllText(
            Path.Combine(_root, ".codex", "config.toml"),
            """
            model = "gpt-5"

            [mcp_servers.Other]
            command = "other"
            """);

        ApplyClientConfig();

        var toml = File.ReadAllText(Path.Combine(_root, ".codex", "config.toml"));

        toml.Should().Contain("model = \"gpt-5\"");
        toml.Should().Contain("[mcp_servers.Other]");
        toml.Should().Contain("[mcp_servers.CodeMeridian]");
        toml.Should().Contain("bearer_token_env_var = \"CodeMeridian_Auth_ApiKey\"");
    }

    [Fact]
    public void ApplyClientConfig_ReplacesExistingCodexCodeMeridianSection()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".codex"));
        File.WriteAllText(
            Path.Combine(_root, ".codex", "config.toml"),
            """
            [mcp_servers.CodeMeridian]
            url = "http://old:5100/sse"
            bearer_token = "literal-secret"

            [mcp_servers.Other]
            command = "other"
            """);

        ApplyClientConfig(port: 5200);

        var toml = File.ReadAllText(Path.Combine(_root, ".codex", "config.toml"));

        toml.Should().Contain("url = \"http://localhost:5200/sse\"");
        toml.Should().NotContain("literal-secret");
        toml.Should().Contain("[mcp_servers.Other]");
    }

    [Fact]
    public void Apply_PreservesExistingEnvValuesAndMergesMissingKeys()
    {
        File.WriteAllText(
            Path.Combine(_root, ".env"),
            """
            NEO4J_PASSWORD=CustomPassword
            CodeMeridian_Auth_ApiKey=ExistingToken
            """);

        Apply();

        var env = File.ReadAllText(Path.Combine(_root, ".env"));

        env.Should().Contain("NEO4J_PASSWORD=CustomPassword");
        env.Should().Contain("CodeMeridian_Auth_ApiKey=ExistingToken");
        env.Should().Contain("CODEMERIDIAN_PORT=5100");
        env.Should().Contain("CodeMeridian_Project=");
        env.Should().Contain("Embedding__Enabled=false");
    }

    [Fact]
    public void Apply_ForceOverwritesEnvDefaultsAndCreatesBackup()
    {
        var envPath = Path.Combine(_root, ".env");
        File.WriteAllText(
            envPath,
            """
            # keep this comment
            CODEMERIDIAN_PORT=4000
            NEO4J_PASSWORD=OldPassword
            CustomKey=CustomValue
            """);

        Apply(port: 5200, force: true);

        var env = File.ReadAllText(envPath);
        env.Should().Contain("# keep this comment");
        env.Should().Contain("CODEMERIDIAN_PORT=5200");
        env.Should().Contain("NEO4J_PASSWORD=CodeMeridian");
        env.Should().Contain("CustomKey=CustomValue");
        env.Should().Contain("CodeMeridian_Auth_ApiKey=");
        Directory.GetFiles(_root, ".env.*.bak").Should().ContainSingle();
    }

    [Fact]
    public void Apply_ForceOverwritesComposeAndCreatesBackup()
    {
        var composePath = Path.Combine(_root, "docker-compose.codemeridian.yml");
        File.WriteAllText(composePath, "services: {}" + Environment.NewLine);

        Apply(force: true);

        File.ReadAllText(composePath).Should().Contain("codemeridian-mcp:");
        Directory.GetFiles(_root, "docker-compose.codemeridian.yml.*.bak").Should().ContainSingle();
    }

    [Fact]
    public void ApplyClientConfig_CreatesOnlyMcpClientFiles()
    {
        var result = new ServeWriter().ApplyClientConfig(new DirectoryInfo(_root), "http://localhost:5100", force: false);

        File.Exists(Path.Combine(_root, ".vscode", "mcp.json")).Should().BeTrue();
        File.Exists(Path.Combine(_root, ".codex", "config.toml")).Should().BeTrue();
        File.Exists(Path.Combine(_root, ".env")).Should().BeFalse();
        File.Exists(Path.Combine(_root, "docker-compose.codemeridian.yml")).Should().BeFalse();
        result.Should().OnlyContain(change => change.Status == "created");
    }

    [Fact]
    public void ApplyClientConfig_WithSelectedTarget_CreatesOnlyThatClientFile()
    {
        var result = new ServeWriter().ApplyClientConfig(
            new DirectoryInfo(_root),
            "http://localhost:5100",
            force: false,
            selectedTargets: [".codex/config.toml"]);

        File.Exists(Path.Combine(_root, ".codex", "config.toml")).Should().BeTrue();
        File.Exists(Path.Combine(_root, ".vscode", "mcp.json")).Should().BeFalse();
        File.Exists(Path.Combine(_root, ".continue", "mcpServers", "code-meridian.yaml")).Should().BeFalse();
        result.Should().ContainSingle(change =>
            change.Path.EndsWith(Path.Combine(".codex", "config.toml"), StringComparison.Ordinal)
            && change.Status == "created");
    }

    [Fact]
    public void ApplyClientConfig_UsesExistingSseUrl()
    {
        new ServeWriter().ApplyClientConfig(new DirectoryInfo(_root), "http://localhost:5100/sse", force: false);

        var json = File.ReadAllText(Path.Combine(_root, ".vscode", "mcp.json"));
        var toml = File.ReadAllText(Path.Combine(_root, ".codex", "config.toml"));
        var yaml = File.ReadAllText(Path.Combine(_root, ".continue", "mcpServers", "code-meridian.yaml"));

        json.Should().Contain("\"url\": \"http://localhost:5100/sse\"");
        toml.Should().Contain("url = \"http://localhost:5100/sse\"");
        yaml.Should().Contain("http://localhost:5100/sse");
        json.Should().NotContain("/sse/sse");
        toml.Should().NotContain("/sse/sse");
        yaml.Should().NotContain("/sse/sse");
    }

    [Fact]
    public void ApplyClientConfig_ForceOverwritesContinueConfigAndCreatesBackup()
    {
        var continueDirectory = Path.Combine(_root, ".continue", "mcpServers");
        Directory.CreateDirectory(continueDirectory);
        var yamlPath = Path.Combine(continueDirectory, "code-meridian.yaml");
        File.WriteAllText(yamlPath, "name: old" + Environment.NewLine);

        new ServeWriter().ApplyClientConfig(new DirectoryInfo(_root), "http://localhost:5100", force: true);

        File.ReadAllText(yamlPath).Should().Contain("http://localhost:5100/sse");
        Directory.GetFiles(continueDirectory, "code-meridian.yaml.*.bak").Should().ContainSingle();
    }

    private ServeApplyResult Apply(
        string host = ServeOptions.DefaultHost,
        int port = ServeOptions.DefaultPort,
        string image = ServeOptions.DefaultImage,
        bool force = false)
    {
        var options = new ServeOptions(
            new DirectoryInfo(_root),
            host,
            port,
            ServeOptions.DefaultNeo4jHttpPort,
            ServeOptions.DefaultNeo4jBoltPort,
            ServeOptions.DefaultComposeFileName,
            image,
            force,
            Start: false);

        return new ServeWriter().Apply(options);
    }

    private IReadOnlyList<ServeFileChange> ApplyClientConfig(
        string host = ServeOptions.DefaultHost,
        int port = ServeOptions.DefaultPort,
        bool force = false)
    {
        return new ServeWriter().ApplyClientConfig(
            new DirectoryInfo(_root),
            $"http://{host}:{port}",
            force);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
