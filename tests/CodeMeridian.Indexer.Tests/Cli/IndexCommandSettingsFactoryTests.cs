using CodeMeridian.Indexer.Cli.Commands;
using CodeMeridian.Indexer.Cli.Configuration;
using CodeMeridian.Tooling.Configuration;
using CodeMeridian.Tooling.Discovery;
using CodeMeridian.Tooling.Storage;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace CodeMeridian.Indexer.Tests.Cli;

[Collection(EnvironmentVariableTestCollection.Name)]
public sealed class IndexCommandSettingsFactoryTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "codemeridian-indexer-settings-tests",
        Guid.NewGuid().ToString("N"));

    public IndexCommandSettingsFactoryTests()
    {
        Directory.CreateDirectory(_root);
        Environment.SetEnvironmentVariable("CodeMeridian_Project", null);
        Environment.SetEnvironmentVariable("CodeMeridian_Url", null);
    }

    [Fact]
    public void Create_FallsBackToGlobalProjectWhenLocalConfigIsBlank()
    {
        var globalRoot = Directory.CreateDirectory(Path.Combine(_root, "global"));
        Environment.SetEnvironmentVariable("CODEMERIDIAN_CONFIG_HOME", globalRoot.FullName);

        File.WriteAllText(
            Path.Combine(globalRoot.FullName, "meridian.json"),
            """
            {
              "project": "GlobalProject",
              "codeMeridianUrl": "http://global:5100"
            }
            """);

        File.WriteAllText(
            Path.Combine(_root, "meridian.json"),
            """
            {
              "project": "   ",
              "codeMeridianUrl": "http://local:5100"
            }
            """);

        var settings = CreateFactory().Create(new IndexCommandOptions(
            Path: _root,
            Project: null,
            CodeMeridianUrl: "http://override:5100",
            Clear: false,
            RebuildKeywords: true,
            IncludeDocs: true,
            Watch: false,
            DryRun: false,
            ListCapabilities: false,
            SkipCSharp: false,
            SkipTypeScript: false,
            SkipConfiguration: false,
            SkipDiagnostics: false,
            AllowRepoScripts: false,
            Incremental: true,
            Storage: null));

        settings.Project.Should().Be("GlobalProject");
        settings.CodeMeridianUrl.Should().Be("http://override:5100");
        settings.RebuildKeywords.Should().BeTrue();
        settings.StorageMode.Should().Be(IndexerStorageMode.Repository);
    }

    [Fact]
    public void Create_IgnoresWhitespaceEnvironmentProject()
    {
        Environment.SetEnvironmentVariable("CodeMeridian_Project", "   ");
        File.WriteAllText(Path.Combine(_root, "Project.sln"), string.Empty);

        var settings = CreateFactory().Create(new IndexCommandOptions(
            Path: _root,
            Project: null,
            CodeMeridianUrl: null,
            Clear: false,
            RebuildKeywords: false,
            IncludeDocs: true,
            Watch: false,
            DryRun: false,
            ListCapabilities: false,
            SkipCSharp: false,
            SkipTypeScript: false,
            SkipConfiguration: false,
            SkipDiagnostics: false,
            AllowRepoScripts: false,
            Incremental: true,
            Storage: null));

        settings.Project.Should().Be("Project");
    }

    [Fact]
    public void Create_UsesGlobalStorageWhenConfigRequestsIt()
    {
        File.WriteAllText(
            Path.Combine(_root, "meridian.json"),
            """
            {
              "project": "MyApi",
              "codeMeridianUrl": "http://local:5100",
              "useGlobalCache": true
            }
            """);

        var settings = CreateFactory().Create(new IndexCommandOptions(
            Path: _root,
            Project: null,
            CodeMeridianUrl: null,
            Clear: false,
            RebuildKeywords: false,
            IncludeDocs: true,
            Watch: false,
            DryRun: false,
            ListCapabilities: false,
            SkipCSharp: false,
            SkipTypeScript: false,
            SkipConfiguration: false,
            SkipDiagnostics: false,
            AllowRepoScripts: false,
            Incremental: true,
            Storage: null));

        settings.StorageMode.Should().Be(IndexerStorageMode.Global);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("CODEMERIDIAN_CONFIG_HOME", null);
        Environment.SetEnvironmentVariable("CodeMeridian_Project", null);
        Environment.SetEnvironmentVariable("CodeMeridian_Url", null);
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static IndexCommandSettingsFactory CreateFactory()
    {
        var fileStore = new CodeMeridianConfigFileStore();
        var discovery = new ProjectDiscoveryService();
        var configuration = new ToolConfigurationService(
            fileStore,
            discovery,
            Options.Create(new ToolCliDefaults()));

        return new IndexCommandSettingsFactory(configuration);
    }
}
