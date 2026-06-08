using CodeMeridian.Indexer.Cli;
using FluentAssertions;

namespace CodeMeridian.Indexer.Tests.Cli;

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
        var exitCode = InitCommand.Run(new DirectoryInfo(_root), "MyApi", "http://localhost:5100", force: false);

        exitCode.Should().Be(0);
        var meridianJsonPath = Path.Combine(_root, "meridian.json");
        File.Exists(meridianJsonPath).Should().BeTrue();
        File.Exists(Path.Combine(_root, "meridian.schema.json")).Should().BeTrue();
        File.Exists(Path.Combine(_root, ".vscode", "mcp.json")).Should().BeTrue();
        File.Exists(Path.Combine(_root, ".codex", "config.toml")).Should().BeTrue();
        File.Exists(Path.Combine(_root, ".env")).Should().BeFalse();
        File.Exists(Path.Combine(_root, "docker-compose.codemeridian.yml")).Should().BeFalse();

        var meridianJson = File.ReadAllText(meridianJsonPath);
        meridianJson.Should().Contain("\"allowRepoScripts\": true");
    }

    [Fact]
    public void RunGlobal_CreatesGlobalMeridianJsonOnly()
    {
        var globalRoot = Directory.CreateDirectory(Path.Combine(_root, "global"));

        var exitCode = InitCommand.RunGlobal("http://global:5100", force: false, globalRoot);

        exitCode.Should().Be(0);
        var meridianJsonPath = Path.Combine(globalRoot.FullName, "meridian.json");
        File.Exists(meridianJsonPath).Should().BeTrue();
        File.Exists(Path.Combine(globalRoot.FullName, "meridian.schema.json")).Should().BeTrue();
        File.Exists(Path.Combine(globalRoot.FullName, ".vscode", "mcp.json")).Should().BeFalse();
        File.Exists(Path.Combine(globalRoot.FullName, ".codex", "config.toml")).Should().BeFalse();

        var meridianJson = File.ReadAllText(meridianJsonPath);
        meridianJson.Should().Contain("\"project\": \"\"");
        meridianJson.Should().Contain("\"codeMeridianUrl\": \"http://global:5100\"");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
