using CodeMeridian.Indexer.Cli;
using CodeMeridian.Indexer.Cli.Commands;
using CodeMeridian.Tooling.Configuration;
using CodeMeridian.Tooling.Discovery;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace CodeMeridian.Indexer.Tests.Cli;

[Collection(EnvironmentVariableTestCollection.Name)]
public sealed class InitCommandTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "codemeridian-init-command-tests",
        Guid.NewGuid().ToString("N"));

    public InitCommandTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void Run_CreatesMeridianJsonAndMcpClientConfigs()
    {
        var exitCode = CreateCommand().Run(new InitCommandOptions(
            Path: _root,
            Project: "MyApi",
            CodeMeridianUrl: "http://localhost:5100",
            Force: false,
            Global: false));

        exitCode.Should().Be(0);
        var meridianJsonPath = Path.Combine(_root, "meridian.json");
        File.Exists(meridianJsonPath).Should().BeTrue();
        File.Exists(Path.Combine(_root, "meridian.schema.json")).Should().BeTrue();
        File.Exists(Path.Combine(_root, ".vscode", "mcp.json")).Should().BeTrue();
        File.Exists(Path.Combine(_root, ".codex", "config.toml")).Should().BeTrue();
        File.Exists(Path.Combine(_root, ".continue", "mcpServers", "code-meridian.yaml")).Should().BeTrue();
        File.Exists(Path.Combine(_root, ".meridian", "architecture.json")).Should().BeTrue();
        File.Exists(Path.Combine(_root, ".meridian", "keyword-classification.json")).Should().BeTrue();
        File.Exists(Path.Combine(_root, ".meridian", "architectures", "architecture.clean.template.json")).Should().BeTrue();
        File.Exists(Path.Combine(_root, ".meridian", "architectures", "architecture.onion.template.json")).Should().BeTrue();
        File.Exists(Path.Combine(_root, ".meridian", "architectures", "architecture.hexagonal.template.json")).Should().BeTrue();
        File.Exists(Path.Combine(_root, ".meridian", "architectures", "architecture.layered.template.json")).Should().BeTrue();
        File.Exists(Path.Combine(_root, ".meridian", "architectures", "architecture.vertical-slice.template.json")).Should().BeTrue();
        File.Exists(Path.Combine(_root, "meridian-agent-capabilities", "agent-capabilities.md")).Should().BeTrue();
        File.Exists(Path.Combine(_root, "meridian-agent-capabilities", "agents", "codemeridian-context-agent.md")).Should().BeTrue();
        File.Exists(Path.Combine(_root, "meridian-agent-capabilities", "skills", "codemeridian-context", "SKILL.md")).Should().BeTrue();
        File.Exists(Path.Combine(_root, "meridian-agent-capabilities", "install-codex-skills.ps1")).Should().BeTrue();
        File.Exists(Path.Combine(_root, "meridian-agent-capabilities", "install-codex-agents.ps1")).Should().BeTrue();
        File.Exists(Path.Combine(_root, ".env")).Should().BeFalse();
        File.Exists(Path.Combine(_root, "docker-compose.codemeridian.yml")).Should().BeFalse();

        var meridianJson = File.ReadAllText(meridianJsonPath);
        meridianJson.Should().Contain("\"version\": 1");
        meridianJson.Should().Contain("\"allowRepoScripts\": true");
        meridianJson.Should().Contain("\"path\": \".meridian/architecture.json\"");
    }

    [Fact]
    public void RunGlobal_EnablesGlobalCacheInProjectConfig()
    {
        var globalRoot = Directory.CreateDirectory(Path.Combine(_root, "global"));
        Environment.SetEnvironmentVariable("CODEMERIDIAN_CONFIG_HOME", globalRoot.FullName);

        var exitCode = CreateCommand().Run(new InitCommandOptions(
            Path: _root,
            Project: null,
            CodeMeridianUrl: "http://global:5100",
            Force: false,
            Global: true));

        exitCode.Should().Be(0);
        var meridianJsonPath = Path.Combine(globalRoot.FullName, "meridian.json");
        File.Exists(meridianJsonPath).Should().BeTrue();
        File.Exists(Path.Combine(globalRoot.FullName, "meridian.schema.json")).Should().BeTrue();
        File.Exists(Path.Combine(globalRoot.FullName, ".meridian", "architecture.json")).Should().BeTrue();
        File.Exists(Path.Combine(globalRoot.FullName, ".meridian", "keyword-classification.json")).Should().BeTrue();
        File.Exists(Path.Combine(globalRoot.FullName, ".meridian", "architectures", "architecture.clean.template.json")).Should().BeTrue();
        File.Exists(Path.Combine(globalRoot.FullName, ".meridian", "architectures", "architecture.onion.template.json")).Should().BeTrue();
        File.Exists(Path.Combine(globalRoot.FullName, ".meridian", "architectures", "architecture.hexagonal.template.json")).Should().BeTrue();
        File.Exists(Path.Combine(globalRoot.FullName, ".meridian", "architectures", "architecture.layered.template.json")).Should().BeTrue();
        File.Exists(Path.Combine(globalRoot.FullName, ".meridian", "architectures", "architecture.vertical-slice.template.json")).Should().BeTrue();
        File.Exists(Path.Combine(globalRoot.FullName, "meridian-agent-capabilities", "agent-capabilities.md")).Should().BeTrue();
        File.Exists(Path.Combine(globalRoot.FullName, "meridian-agent-capabilities", "agents", "codemeridian-context-agent.md")).Should().BeTrue();
        File.Exists(Path.Combine(globalRoot.FullName, "meridian-agent-capabilities", "skills", "codemeridian-context", "SKILL.md")).Should().BeTrue();
        File.Exists(Path.Combine(globalRoot.FullName, "meridian-agent-capabilities", "install-codex-skills.ps1")).Should().BeTrue();
        File.Exists(Path.Combine(globalRoot.FullName, "meridian-agent-capabilities", "install-codex-agents.ps1")).Should().BeTrue();
        File.Exists(Path.Combine(_root, "meridian.json")).Should().BeFalse();
        File.Exists(Path.Combine(_root, ".vscode", "mcp.json")).Should().BeFalse();
        File.Exists(Path.Combine(_root, ".codex", "config.toml")).Should().BeFalse();
        File.Exists(Path.Combine(_root, ".continue", "mcpServers", "code-meridian.yaml")).Should().BeFalse();

        var meridianJson = File.ReadAllText(meridianJsonPath);
        meridianJson.Should().Contain("\"version\": 1");
        meridianJson.Should().Contain("\"project\": \"\"");
        meridianJson.Should().Contain("\"codeMeridianUrl\": \"http://global:5100\"");
        meridianJson.Should().Contain("\"useGlobalCache\": true");
    }

    [Fact]
    public void Run_WhenMeridianJsonAlreadyExists_RefreshesItWithoutForce()
    {
        var meridianJsonPath = Path.Combine(_root, "meridian.json");
        File.WriteAllText(
            meridianJsonPath,
            """
            {
              "project": "MyApi",
              "codeMeridianUrl": "http://localhost:5100",
              "configurationFiles": [".env"]
            }
            """);

        var exitCode = CreateCommand().Run(new InitCommandOptions(
            Path: _root,
            Project: "MyApi",
            CodeMeridianUrl: "http://localhost:5100",
            Force: false,
            Global: false));

        exitCode.Should().Be(0);
        var meridianJson = File.ReadAllText(meridianJsonPath);
        meridianJson.Should().Contain("\"version\": 1");
        meridianJson.Should().Contain("\"allowRepoScripts\": true");
        File.Exists($"{meridianJsonPath}.bak").Should().BeTrue();
    }

    [Fact]
    public void Run_PrintsSelectedArchitectureAndAvailableTemplates()
    {
        var writer = new StringWriter();
        var originalOut = Console.Out;

        try
        {
            Console.SetOut(writer);

            var exitCode = CreateCommand().Run(new InitCommandOptions(
                Path: _root,
                Project: "MyApi",
                CodeMeridianUrl: "http://localhost:5100",
                Force: false,
                Global: false));

            exitCode.Should().Be(0);
            var output = writer.ToString();
            output.Should().Contain("Architecture selected");
            output.Should().Contain(".meridian/architecture.json");
            output.Should().Contain("architecture.clean.template.json");
            output.Should().Contain("architecture.vertical-slice.template.json");
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("CODEMERIDIAN_CONFIG_HOME", null);
        if (!Directory.Exists(_root))
            return;

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Windows can briefly lock the generated config file; the temp tree is disposable.
        }
    }

    private static InitCommand CreateCommand()
    {
        var fileStore = new CodeMeridianConfigFileStore();
        var discovery = new ProjectDiscoveryService();
        var configuration = new ToolConfigurationService(
            fileStore,
            discovery,
            Options.Create(new ToolCliDefaults()));

        return new InitCommand(configuration, fileStore, discovery, new ServeWriter());
    }
}
