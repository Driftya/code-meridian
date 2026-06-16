using CodeMeridian.Tooling.Configuration;
using FluentAssertions;

namespace CodeMeridian.Indexer.Tests.Cli;

public sealed class IndexerConfigTests : IDisposable
{
    private readonly CodeMeridianConfigFileStore _store = new();
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "codemeridian-indexer-config-tests",
        Guid.NewGuid().ToString("N"));

    public IndexerConfigTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void Load_UsesProjectAndUrlFromMeridianJson()
    {
        File.WriteAllText(
            Path.Combine(_root, "meridian.json"),
            """
            {
              "project": "MyApi",
              "codeMeridianUrl": "http://localhost:5100",
              "allowRepoScripts": true,
              "useGlobalCache": true,
              "configurationFiles": [".env", "appsettings.*.json"],
              "indexing": {
                "fileRoles": {
                  "generated": ["**/*.g.cs"],
                  "test": ["**/*.spec.ts"]
                }
              }
            }
            """);

        var result = _store.LoadLocal(new DirectoryInfo(_root));

        result.Should().NotBeNull();
        result!.Project.Should().Be("MyApi");
        result.CodeMeridianUrl.Should().Be("http://localhost:5100");
        result.AllowRepoScripts.Should().BeTrue();
        result.UseGlobalCache.Should().BeTrue();
        result.ConfigurationFiles.Should().BeEquivalentTo([".env", "appsettings.*.json"]);
        result.ArchitecturePath.Should().BeNull();
        result.FileRoles.Should().NotBeNull();
        result.FileRoles!.Generated.Should().BeEquivalentTo(["**/*.g.cs"]);
        result.FileRoles.Test.Should().BeEquivalentTo(["**/*.spec.ts"]);
        result.Version.Should().Be(0);
    }

    [Fact]
    public void Load_SupportsUrlAlias()
    {
        File.WriteAllText(
            Path.Combine(_root, "meridian.json"),
            """
            {
              "project": "MyApi",
              "url": "http://192.168.1.10:5100",
              "useGlobalCache": false
            }
            """);

        var result = _store.LoadLocal(new DirectoryInfo(_root));

        result.Should().NotBeNull();
        result!.CodeMeridianUrl.Should().Be("http://192.168.1.10:5100");
        result.UseGlobalCache.Should().BeFalse();
        result.Version.Should().Be(0);
    }

    [Fact]
    public void LoadGlobal_UsesProjectAndUrlFromGlobalMeridianJson()
    {
        var globalRoot = Directory.CreateDirectory(Path.Combine(_root, "global"));
        File.WriteAllText(
            Path.Combine(globalRoot.FullName, "meridian.json"),
            """
            {
              "project": "GlobalProject",
              "codeMeridianUrl": "http://global:5100",
              "allowRepoScripts": true,
              "useGlobalCache": true
            }
            """);

        var result = _store.LoadGlobal(globalRoot);

        result.Should().NotBeNull();
        result!.Project.Should().Be("GlobalProject");
        result.CodeMeridianUrl.Should().Be("http://global:5100");
        result.AllowRepoScripts.Should().BeTrue();
        result.UseGlobalCache.Should().BeTrue();
        result.Version.Should().Be(0);
    }

    [Fact]
    public void LoadLocal_ReadsNearestLocalConfig()
    {
        var child = Directory.CreateDirectory(Path.Combine(_root, "src", "app"));
        File.WriteAllText(Path.Combine(_root, "meridian.json"), """
            {
              "project": "LocalApi",
              "codeMeridianUrl": "http://local:5100"
            }
            """);

        var result = _store.LoadLocal(child);

        result.Should().NotBeNull();
        result!.Project.Should().Be("LocalApi");
        result.CodeMeridianUrl.Should().Be("http://local:5100");
        result.Version.Should().Be(0);
    }

    [Fact]
    public void Write_CreatesMeridianJson()
    {
        _store.Write(new DirectoryInfo(_root), "MyApi", "http://localhost:5100", overwrite: false);

        var json = File.ReadAllText(Path.Combine(_root, "meridian.json"));

        json.Should().Contain("\"version\": 1");
        json.Should().Contain("\"project\": \"MyApi\"");
        json.Should().Contain("\"$schema\": \"./meridian.schema.json\"");
        json.Should().Contain("\"codeMeridianUrl\": \"http://localhost:5100\"");
        json.Should().Contain("\"allowRepoScripts\": true");
        json.Should().Contain("\"useGlobalCache\": false");
        json.Should().Contain("\"architecture\"");
        json.Should().Contain("\"path\": \".meridian/architecture.json\"");
        json.Should().Contain("\"analysis\"");
        json.Should().Contain("\"indexing\"");
        json.Should().Contain("\"fileRoles\"");
        json.Should().Contain("\"buildArtifact\"");
        json.Should().Contain("\"skipHeuristicSourcePrefixes\"");
        json.Should().Contain("\"preferProductionOverTests\": true");
        json.Should().Contain("\"DependencyInjection\"");
        json.Should().Contain("\"Startup\"");
        json.Should().Contain("\"CompositionRoot\"");
        File.Exists(Path.Combine(_root, ".meridian", "architecture.json")).Should().BeTrue();
        File.Exists(Path.Combine(_root, ".meridian", "architectures", "architecture.clean.template.json")).Should().BeTrue();
        File.Exists(Path.Combine(_root, ".meridian", "architectures", "architecture.onion.template.json")).Should().BeTrue();
        File.Exists(Path.Combine(_root, ".meridian", "architectures", "architecture.hexagonal.template.json")).Should().BeTrue();
        File.Exists(Path.Combine(_root, ".meridian", "architectures", "architecture.layered.template.json")).Should().BeTrue();
        File.Exists(Path.Combine(_root, ".meridian", "architectures", "architecture.vertical-slice.template.json")).Should().BeTrue();
    }

    [Fact]
    public void WriteGlobal_CreatesGlobalMeridianJsonWithoutProjectName()
    {
        var globalRoot = Directory.CreateDirectory(Path.Combine(_root, "global"));

        _store.WriteGlobal("http://global:5100", overwrite: false, globalRoot);

        var json = File.ReadAllText(Path.Combine(globalRoot.FullName, "meridian.json"));
        json.Should().Contain("\"version\": 1");
        json.Should().Contain("\"project\": \"\"");
        json.Should().Contain("\"codeMeridianUrl\": \"http://global:5100\"");
        json.Should().Contain("\"useGlobalCache\": true");
        File.Exists(Path.Combine(globalRoot.FullName, "meridian.schema.json")).Should().BeTrue();
    }

    [Fact]
    public void WriteGlobal_UpdatesExistingGlobalConfigAndPreservesUnrelatedFields()
    {
        var globalRoot = Directory.CreateDirectory(Path.Combine(_root, "global"));
        File.WriteAllText(
            Path.Combine(globalRoot.FullName, "meridian.json"),
            """
            {
              "project": "OldProject",
              "codeMeridianUrl": "http://old:5100",
              "allowRepoScripts": true,
              "customSetting": "keep-me"
            }
            """);

        _store.WriteGlobal("http://new:5100", overwrite: false, globalRoot);

        var json = File.ReadAllText(Path.Combine(globalRoot.FullName, "meridian.json"));
        json.Should().Contain("\"version\": 1");
        json.Should().Contain("\"project\": \"\"");
        json.Should().Contain("\"codeMeridianUrl\": \"http://new:5100\"");
        json.Should().Contain("\"useGlobalCache\": true");
        json.Should().Contain("\"allowRepoScripts\": true");
        json.Should().Contain("\"customSetting\": \"keep-me\"");
    }

    [Fact]
    public void Write_MergesMissingDefaultsIntoExistingLegacyConfigAndCreatesBackup()
    {
        var configPath = Path.Combine(_root, "meridian.json");
        File.WriteAllText(
            configPath,
            """
            {
              "project": "MyApi",
              "codeMeridianUrl": "http://localhost:5100",
              "configurationFiles": [
                ".env"
              ],
              "analysis": {
                "ranking": {
                  "infrastructureNames": [
                    "CustomRoot"
                  ]
                }
              },
              "customSetting": {
                "enabled": true
              }
            }
            """);

        var result = _store.Write(new DirectoryInfo(_root), "MyApi", "http://localhost:5100", overwrite: false);

        result.Created.Should().BeFalse();
        result.Changed.Should().BeTrue();
        result.PreviousVersion.Should().Be(0);
        result.CurrentVersion.Should().Be(1);
        result.BackupPath.Should().NotBeNull();
        File.Exists(result.BackupPath!).Should().BeTrue();

        var json = File.ReadAllText(configPath);
        json.Should().Contain("\"version\": 1");
        json.Should().Contain("\"customSetting\"");
        json.Should().Contain("\"enabled\": true");
        json.Should().Contain("\"CustomRoot\"");
        json.Should().Contain("\"DependencyInjection\"");
        json.Should().Contain("\"allowRepoScripts\": true");
        json.Should().Contain("\"skipHeuristicSourcePrefixes\"");
        json.Should().Contain("\"fileRoles\"");
        json.Should().Contain("\"architecture\"");
        json.Should().Contain("\"meridian.sample.json\"");
        json.Should().Contain("\".env\"");
        json.Should().Contain("\"appsettings.json\"");
    }

    [Fact]
    public void Write_DoesNotDuplicateExistingArrayEntriesDuringRefresh()
    {
        var configPath = Path.Combine(_root, "meridian.json");
        File.WriteAllText(
            configPath,
            """
            {
              "project": "MyApi",
              "codeMeridianUrl": "http://localhost:5100",
              "configurationFiles": [
                ".env",
                "appsettings.json"
              ],
              "analysis": {
                "ranking": {
                  "infrastructureNames": [
                    "DependencyInjection"
                  ]
                }
              }
            }
            """);

        _store.Write(new DirectoryInfo(_root), "MyApi", "http://localhost:5100", overwrite: false);

        var json = File.ReadAllText(configPath);
        json.Should().Contain("\"version\": 1");
        json.Split("\"appsettings.json\"").Length.Should().Be(2);
        json.Split("\"DependencyInjection\"").Length.Should().Be(2);
    }

    [Fact]
    public void Write_WhenAlreadyCurrent_DoesNotRewriteFile()
    {
        _store.Write(new DirectoryInfo(_root), "MyApi", "http://localhost:5100", overwrite: false);

        var result = _store.Write(new DirectoryInfo(_root), "MyApi", "http://localhost:5100", overwrite: false);

        result.Created.Should().BeFalse();
        result.Changed.Should().BeFalse();
        result.BackupPath.Should().BeNull();
        result.AddedPaths.Should().BeEmpty();
    }

    [Fact]
    public void Write_WhenExistingJsonIsInvalid_FailsWithoutWriting()
    {
        var configPath = Path.Combine(_root, "meridian.json");
        File.WriteAllText(configPath, "{ invalid json");

        var act = () => _store.Write(new DirectoryInfo(_root), "MyApi", "http://localhost:5100", overwrite: false);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"Config file is not valid JSON: {configPath}");
        File.ReadAllText(configPath).Should().Be("{ invalid json");
        File.Exists($"{configPath}.bak").Should().BeFalse();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
