using CodeMeridian.Indexer.Cli.Commands;
using CodeMeridian.Indexer.Cli.Composition;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace CodeMeridian.Indexer.Tests.Cli;

[Collection(EnvironmentVariableTestCollection.Name)]
public sealed class RootCommandFactoryTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateDirectory(Path.Combine(
        Path.GetTempPath(),
        $"codemeridian-root-command-{Guid.NewGuid():N}"));

    [Fact]
    public async Task IndexDryRun_DefaultsToRebuildKeywords()
    {
        File.WriteAllText(Path.Combine(_root.FullName, "App.cs"), "class App {}");
        var output = await InvokeAsync(_root.FullName, "--dry-run", "--skip-diagnostics");

        output.Should().Contain("Rebuild keywords  : True");
    }

    [Fact]
    public async Task IndexDryRun_WithSkipKeywords_DisablesKeywordRebuild()
    {
        File.WriteAllText(Path.Combine(_root.FullName, "App.cs"), "class App {}");
        var output = await InvokeAsync(_root.FullName, "--dry-run", "--skip-diagnostics", "--skip-keywords");

        output.Should().Contain("Rebuild keywords  : False");
    }

    private static async Task<string> InvokeAsync(params string[] args)
    {
        var services = new ServiceCollection();
        services.AddIndexerCli();
        await using var provider = services.BuildServiceProvider();
        var root = provider.GetRequiredService<RootCommandFactory>().Create();

        var output = new StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(output);

        try
        {
            var exitCode = await root.Parse(args).InvokeAsync();
            exitCode.Should().Be(0);
            return output.ToString();
        }
        finally
        {
            Console.SetOut(originalOut);
            output.Dispose();
        }
    }

    public void Dispose()
    {
        if (_root.Exists)
            _root.Delete(recursive: true);
    }
}
