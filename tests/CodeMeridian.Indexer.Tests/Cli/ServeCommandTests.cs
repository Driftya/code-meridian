using CodeMeridian.Indexer.Cli;
using CodeMeridian.Indexer.Cli.Commands;
using FluentAssertions;

namespace CodeMeridian.Indexer.Tests.Cli;

public sealed class ServeCommandTests
{
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
}
