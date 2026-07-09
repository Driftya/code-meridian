using CodeMeridian.Indexer.Cli;
using CodeMeridian.Indexer.Cli.Commands;
using FluentAssertions;

namespace CodeMeridian.Indexer.Tests.Cli;

public sealed class ServeCommandTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "codemeridian-serve-command-tests",
        Guid.NewGuid().ToString("N"));

    public ServeCommandTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void BuildDockerCommands_PullsBeforeStartingCompose()
    {
        var commands = ServeCommand.BuildDockerCommands("docker-compose.codemeridian.yml");

        commands.Should().HaveCount(2);
        commands[0].Should().Equal("compose", "-f", "docker-compose.codemeridian.yml", "pull");
        commands[1].Should().Equal("compose", "-f", "docker-compose.codemeridian.yml", "up", "-d");
    }

    [Fact]
    public void FormatCommand_QuotesComposePathWithWhitespace()
    {
        var command = ServeCommand.FormatCommand(["compose", "-f", @"C:\Temp Folder\docker-compose.codemeridian.yml", "pull"]);

        command.Should().Be("docker compose -f \"C:\\Temp Folder\\docker-compose.codemeridian.yml\" pull");
    }

    [Fact]
    public async Task RunAsync_WhenStartIsFalse_WritesFilesAndNextSteps()
    {
        var command = new ServeCommand(new ServeWriter());
        var options = new ServeOptions(
            new DirectoryInfo(_root),
            "127.0.0.1",
            5200,
            47475,
            47688,
            "docker-compose.codemeridian.yml",
            "ghcr.io/driftya/codemeridian-mcp:test",
            Force: false,
            Start: false);
        var originalOut = Console.Out;
        using var writer = new StringWriter();

        try
        {
            Console.SetOut(writer);

            var exitCode = await command.RunAsync(options);

            exitCode.Should().Be(0);
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        File.Exists(Path.Combine(_root, ".env")).Should().BeTrue();
        File.Exists(Path.Combine(_root, "docker-compose.codemeridian.yml")).Should().BeTrue();

        var output = writer.ToString();
        output.Should().Contain("CodeMeridian serve");
        output.Should().Contain("Next step:");
        output.Should().Contain("docker compose -f");
        output.Should().Contain("pull");
        output.Should().Contain("up -d");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
