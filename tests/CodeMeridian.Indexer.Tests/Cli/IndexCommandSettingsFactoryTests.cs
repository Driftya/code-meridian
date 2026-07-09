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
        Environment.SetEnvironmentVariable("CodeMeridian_Auth_ApiKey", null);
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
        settings.HasOutdatedLocalConfig.Should().BeTrue();
        settings.LocalConfigVersion.Should().Be(0);
        settings.CurrentConfigVersion.Should().Be(CodeMeridianConfigFileStore.CurrentConfigVersion);
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
        settings.HasOutdatedLocalConfig.Should().BeTrue();
        settings.LocalConfigVersion.Should().Be(0);
        settings.CurrentConfigVersion.Should().Be(CodeMeridianConfigFileStore.CurrentConfigVersion);
    }

    [Fact]
    public void Create_DoesNotFlagCurrentLocalConfigAsOutdated()
    {
        File.WriteAllText(
            Path.Combine(_root, "meridian.json"),
            """
            {
              "version": 1,
              "project": "MyApi",
              "codeMeridianUrl": "http://local:5100"
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

        settings.HasOutdatedLocalConfig.Should().BeFalse();
        settings.LocalConfigVersion.Should().Be(1);
        settings.CurrentConfigVersion.Should().Be(CodeMeridianConfigFileStore.CurrentConfigVersion);
    }

    [Fact]
    public void Create_UsesConfiguredArchitecturePath()
    {
        File.WriteAllText(
            Path.Combine(_root, "meridian.json"),
            """
            {
              "version": 1,
              "project": "MyApi",
              "codeMeridianUrl": "http://local:5100",
              "architecture": {
                "path": ".meridian/architecture.custom.json"
              }
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

        settings.ArchitecturePath.Should().Be(".meridian/architecture.custom.json");
    }

    [Fact]
    public void Create_UsesGlobalFallbacksForConfigurationFilesArchitectureAndFileRoles()
    {
        var globalRoot = Directory.CreateDirectory(Path.Combine(_root, "global"));
        Environment.SetEnvironmentVariable("CODEMERIDIAN_CONFIG_HOME", globalRoot.FullName);

        File.WriteAllText(
            Path.Combine(globalRoot.FullName, "meridian.json"),
            """
            {
              "version": 1,
              "project": "GlobalProject",
              "codeMeridianUrl": "http://global:5100",
              "configurationFiles": [".env", "appsettings.json"],
              "architecture": {
                "path": ".meridian/global-architecture.json"
              },
              "indexing": {
                "fileRoles": {
                  "generated": ["**/*.g.cs"],
                  "configuration": ["appsettings.json"]
                }
              }
            }
            """);

        File.WriteAllText(
            Path.Combine(_root, "meridian.json"),
            """
            {
              "version": 1,
              "project": "LocalProject",
              "codeMeridianUrl": "http://local:5100"
            }
            """);

        var settings = CreateFactory().Create(CreateOptions());

        settings.ConfigurationFiles.Should().BeEquivalentTo([".env", "appsettings.json"]);
        settings.ArchitecturePath.Should().Be(".meridian/global-architecture.json");
        settings.FileRoles.Should().NotBeNull();
        settings.FileRoles!.Generated.Should().BeEquivalentTo(["**/*.g.cs"]);
        settings.FileRoles.Configuration.Should().BeEquivalentTo(["appsettings.json"]);
    }

    [Fact]
    public void Create_UsesLocalConfigurationFilesAndFileRolesAheadOfGlobal()
    {
        var globalRoot = Directory.CreateDirectory(Path.Combine(_root, "global"));
        Environment.SetEnvironmentVariable("CODEMERIDIAN_CONFIG_HOME", globalRoot.FullName);

        File.WriteAllText(
            Path.Combine(globalRoot.FullName, "meridian.json"),
            """
            {
              "version": 1,
              "project": "GlobalProject",
              "codeMeridianUrl": "http://global:5100",
              "configurationFiles": [".env"],
              "indexing": {
                "fileRoles": {
                  "generated": ["**/*.g.cs"]
                }
              }
            }
            """);

        File.WriteAllText(
            Path.Combine(_root, "meridian.json"),
            """
            {
              "version": 1,
              "project": "LocalProject",
              "codeMeridianUrl": "http://local:5100",
              "configurationFiles": ["package.json"],
              "indexing": {
                "fileRoles": {
                  "configuration": ["package.json"]
                }
              }
            }
            """);

        var settings = CreateFactory().Create(CreateOptions());

        settings.ConfigurationFiles.Should().BeEquivalentTo(["package.json"]);
        settings.FileRoles.Should().NotBeNull();
        settings.FileRoles!.Configuration.Should().BeEquivalentTo(["package.json"]);
        settings.FileRoles.Generated.Should().BeNull();
    }

    [Fact]
    public void Create_UsesDefaultArchitecturePathWhenNoConfigProvidesOne()
    {
        File.WriteAllText(
            Path.Combine(_root, "meridian.json"),
            """
            {
              "version": 1,
              "project": "MyApi",
              "codeMeridianUrl": "http://local:5100"
            }
            """);

        var settings = CreateFactory().Create(CreateOptions());

        settings.ArchitecturePath.Should().Be(CodeMeridianConfigFileStore.DefaultArchitecturePath);
    }

    [Fact]
    public void Create_RespectsExplicitStorageOverride()
    {
        File.WriteAllText(
            Path.Combine(_root, "meridian.json"),
            """
            {
              "version": 1,
              "project": "MyApi",
              "codeMeridianUrl": "http://local:5100",
              "useGlobalCache": false
            }
            """);

        var settings = CreateFactory().Create(CreateOptions() with { Storage = IndexerStorageMode.Global });

        settings.StorageMode.Should().Be(IndexerStorageMode.Global);
    }

    [Fact]
    public void Create_UsesDefaultUrlWhenNothingElseProvidesOne()
    {
        var root = new DirectoryInfo(_root);
        var context = new ToolConfigurationContext(
            root,
            LocalConfig: null,
            GlobalConfig: null,
            EnvironmentProject: null,
            EnvironmentUrl: null,
            ApiKey: null);
        var configuration = new StubToolConfigurationService(
            context,
            resolvedProject: "Project",
            resolvedCodeMeridianUrl: "http://localhost:5100");

        var settings = new IndexCommandSettingsFactory(configuration).Create(CreateOptions());

        settings.CodeMeridianUrl.Should().Be("http://localhost:5100");
    }

    [Fact]
    public void Create_FallsBackToFolderNameWhenProjectCannotBeResolvedFromFilesOrConfig()
    {
        var rootWithoutNameHints = Directory.CreateDirectory(Path.Combine(_root, "blank"));

        var settings = CreateFactory().Create(CreateOptions() with
        {
            Path = rootWithoutNameHints.FullName
        });

        settings.Project.Should().Be("blank");
    }

    [Fact]
    public void Create_PreservesExistingApiKeyWhenTargetRepoDotEnvDefinesAnotherValue()
    {
        var invocationRoot = Directory.CreateDirectory(Path.Combine(_root, "invocation"));
        var targetRoot = Directory.CreateDirectory(Path.Combine(_root, "target"));
        File.WriteAllText(Path.Combine(invocationRoot.FullName, ".env"), "CodeMeridian_Auth_ApiKey=from-current-dotenv");
        File.WriteAllText(Path.Combine(targetRoot.FullName, ".env"), "CodeMeridian_Auth_ApiKey=from-target-dotenv");
        Environment.SetEnvironmentVariable("CodeMeridian_Auth_ApiKey", "from-shell");

        var originalDirectory = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(invocationRoot.FullName);

            var settings = CreateFactory().Create(new IndexCommandOptions(
                Path: targetRoot.FullName,
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

            settings.ApiKey.Should().Be("from-shell");
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDirectory);
        }
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("CODEMERIDIAN_CONFIG_HOME", null);
        Environment.SetEnvironmentVariable("CodeMeridian_Project", null);
        Environment.SetEnvironmentVariable("CodeMeridian_Url", null);
        Environment.SetEnvironmentVariable("CodeMeridian_Auth_ApiKey", null);
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

    private IndexCommandOptions CreateOptions() => new(
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
        Storage: null);

    private sealed class StubToolConfigurationService(
        ToolConfigurationContext context,
        string resolvedProject,
        string resolvedCodeMeridianUrl,
        bool allowRepoScripts = false) : IToolConfigurationService
    {
        public ToolConfigurationContext CreateContext(string? path) => context;

        public string ResolveProject(ToolConfigurationContext context, string? overrideProject, bool includeFallback = true) =>
            resolvedProject;

        public string ResolveCodeMeridianUrl(ToolConfigurationContext context, string? overrideUrl) =>
            resolvedCodeMeridianUrl;

        public bool ResolveAllowRepoScripts(ToolConfigurationContext context, bool allowRepoScriptsOverride) =>
            allowRepoScripts;

        public DirectoryInfo ResolveRootPath(string? path) => context.RootPath;
    }
}
