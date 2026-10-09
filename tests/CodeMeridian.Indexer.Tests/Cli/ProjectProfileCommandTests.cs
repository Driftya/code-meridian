using System.Text.Json;
using CodeMeridian.Core.Projects;
using CodeMeridian.Indexer.Cli.Commands;
using CodeMeridian.Indexer.Cli.Composition;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace CodeMeridian.Indexer.Tests.Cli;

[Collection(EnvironmentVariableTestCollection.Name)]
public sealed class ProjectProfileCommandTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateDirectory(Path.Combine(
        Path.GetTempPath(), $"codemeridian-profile-command-{Guid.NewGuid():N}"));

    [Fact]
    public async Task Profile_ProducesVersionedJsonWithoutIndexingOrCreatingFiles()
    {
        File.WriteAllText(Path.Combine(_root.FullName, "chapter.md"), "# Chapter");
        File.WriteAllText(Path.Combine(_root.FullName, "Publish.ps1"), "throw 'must not run'");
        var before = Directory.GetFiles(_root.FullName, "*", SearchOption.AllDirectories);

        var result = await InvokeAsync("profile", _root.FullName, "--project", "Novel", "--format", "json");
        var profile = JsonSerializer.Deserialize<ProjectProfile>(result.Output, ProjectProfileCommand.JsonOptions)!;

        result.ExitCode.Should().Be(0);
        profile.ContractVersion.Should().Be("1.0");
        profile.Project.Should().Be("Novel");
        profile.FileKinds.Select(kind => kind.Kind).Should().Equal("markdown", "powershell");
        profile.Analyzers.Should().OnlyContain(analyzer => analyzer.IndexingState == "unknown");
        Directory.GetFiles(_root.FullName, "*", SearchOption.AllDirectories).Should().BeEquivalentTo(before);
        Directory.Exists(Path.Combine(_root.FullName, ".meridian")).Should().BeFalse();
    }

    [Fact]
    public async Task Profile_UsesConfiguredProjectAndSqlEnablement()
    {
        File.WriteAllText(Path.Combine(_root.FullName, "meridian.json"),
            """{"version":2,"project":"Configured","codeMeridianUrl":"http://localhost:1","indexing":{"sql":{"enabled":true,"defaultDialect":"postgresql","databaseScope":"default"}}}""");
        File.WriteAllText(Path.Combine(_root.FullName, "query.sql"), "select 1;");

        var result = await InvokeAsync("profile", _root.FullName, "--project", "Override", "--format", "json", "--skip-sql");
        var profile = JsonSerializer.Deserialize<ProjectProfile>(result.Output, ProjectProfileCommand.JsonOptions)!;

        result.ExitCode.Should().Be(0);
        profile.Project.Should().Be("Override");
        profile.FileKinds.Should().Contain(kind => kind.Kind == "sql");
        profile.Analyzers.Single(analyzer => analyzer.Id == "sql").ConfigurationState.Should().Be("disabled");
    }

    [Fact]
    public async Task Profile_TextExplainsLocalEvidenceAndUnknownReadiness()
    {
        File.WriteAllText(Path.Combine(_root.FullName, "README.md"), "# Docs");

        var result = await InvokeAsync("profile", _root.FullName, "--project", "Docs");

        result.ExitCode.Should().Be(0);
        result.Output.Should().Contain("local_discovery").And.Contain("markdown: 1")
            .And.Contain("server evidence has not been queried").And.Contain("powershell: support=unsupported");
    }

    [Fact]
    public async Task Profile_InvalidFormatIsRejected()
    {
        var result = await InvokeAsync("profile", _root.FullName, "--format", "xml");

        result.ExitCode.Should().Be(1);
        result.Error.Should().Contain("--format must be text or json");
        result.Output.Should().BeEmpty();
    }

    [Fact]
    public async Task Profile_MissingRootIsRejected()
    {
        var result = await InvokeAsync("profile", Path.Combine(_root.FullName, "missing"), "--project", "Missing");

        result.ExitCode.Should().Be(1);
        result.Error.Should().Contain("Directory not found");
        result.Output.Should().BeEmpty();
    }

    private static async Task<(int ExitCode, string Output, string Error)> InvokeAsync(params string[] args)
    {
        var services = new ServiceCollection();
        services.AddIndexerCli();
        await using var provider = services.BuildServiceProvider();
        var command = provider.GetRequiredService<RootCommandFactory>().Create();
        using var output = new StringWriter();
        using var error = new StringWriter();
        var originalOut = Console.Out;
        var originalError = Console.Error;
        Console.SetOut(output);
        Console.SetError(error);
        try
        {
            var exitCode = await command.Parse(args).InvokeAsync();
            return (exitCode, output.ToString(), error.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    public void Dispose() => _root.Delete(recursive: true);
}
