using System.Text.Json;
using CodeMeridian.Tooling.Configuration;
using FluentAssertions;

namespace CodeMeridian.Indexer.Tests.Cli;

[Collection(EnvironmentVariableTestCollection.Name)]
public sealed class CodeMeridianConfigFileStoreTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateDirectory(Path.Combine(
        Path.GetTempPath(),
        "codemeridian-config-store-tests",
        Guid.NewGuid().ToString("N")));

    [Fact]
    public void LoadLocal_LoadsNearestConfigAndNormalizesPatterns()
    {
        var parent = Directory.CreateDirectory(Path.Combine(_root.FullName, "parent"));
        var child = Directory.CreateDirectory(Path.Combine(parent.FullName, "src", "feature"));
        File.WriteAllText(Path.Combine(parent.FullName, "meridian.json"), """
            {
              "version": 7,
              "project": "  LocalProject  ",
              "codeMeridianUrl": "  http://local  ",
              "allowRepoScripts": true,
              "useGlobalCache": false,
              "configurationFiles": [
                ".env",
                " appsettings.json ",
                ".ENV",
                "   "
              ],
              "architecture": {
                "path": "  .meridian/custom-architecture.json  "
              },
              "indexing": {
                "fileRoles": {
                  "test": [
                    " tests/**/*.cs ",
                    "**/*Tests.cs",
                    "TESTS/**/*.CS",
                    ""
                  ],
                  "generated": [
                    " **/*.g.cs "
                  ]
                }
              }
            }
            """);

        var sut = new CodeMeridianConfigFileStore();

        var snapshot = sut.LoadLocal(child);

        snapshot.Should().NotBeNull();
        snapshot!.Project.Should().Be("LocalProject");
        snapshot.CodeMeridianUrl.Should().Be("http://local");
        snapshot.AllowRepoScripts.Should().BeTrue();
        snapshot.UseGlobalCache.Should().BeFalse();
        snapshot.Version.Should().Be(7);
        snapshot.ArchitecturePath.Should().Be(".meridian/custom-architecture.json");
        snapshot.ConfigurationFiles.Should().BeEquivalentTo([".env", "appsettings.json"]);
        snapshot.FileRoles.Should().NotBeNull();
        snapshot.FileRoles!.Test.Should().BeEquivalentTo(["tests/**/*.cs", "**/*Tests.cs"]);
        snapshot.FileRoles.Generated.Should().BeEquivalentTo(["**/*.g.cs"]);
    }

    [Fact]
    public void GetGlobalConfigDirectory_PrefersEnvironmentOverride()
    {
        var expectedDirectory = Directory.CreateDirectory(Path.Combine(_root.FullName, "global-home"));
        var original = Environment.GetEnvironmentVariable("CODEMERIDIAN_CONFIG_HOME");

        try
        {
            Environment.SetEnvironmentVariable("CODEMERIDIAN_CONFIG_HOME", expectedDirectory.FullName);

            var result = new CodeMeridianConfigFileStore().GetGlobalConfigDirectory();

            result.FullName.Should().Be(expectedDirectory.FullName);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CODEMERIDIAN_CONFIG_HOME", original);
        }
    }

    [Fact]
    public void Write_MergesMissingDefaultsAndCreatesBackup()
    {
        var meridianJsonPath = Path.Combine(_root.FullName, "meridian.json");
        File.WriteAllText(meridianJsonPath, """
            {
              "project": "ExistingProject",
              "codeMeridianUrl": "http://existing",
              "configurationFiles": [
                ".env",
                "custom.settings.json",
                ".ENV"
              ],
              "indexing": {
                "fileRoles": {
                  "test": [
                    "tests/**/*.cs"
                  ],
                  "configuration": [
                    "**/*Config.cs"
                  ]
                }
              }
            }
            """);

        var sut = new CodeMeridianConfigFileStore();

        var result = sut.Write(_root, "IgnoredProject", "http://ignored", useGlobalCache: false, overwrite: false);

        result.Created.Should().BeFalse();
        result.Changed.Should().BeTrue();
        result.PreviousVersion.Should().Be(0);
        result.CurrentVersion.Should().Be(CodeMeridianConfigFileStore.CurrentConfigVersion);
        result.BackupPath.Should().NotBeNull();
        result.AddedPaths.Should().Contain("version");
        result.AddedPaths.Should().Contain("architecture");
        result.AddedPaths.Should().Contain("analysis");
        File.Exists(result.BackupPath!).Should().BeTrue();

        using var document = JsonDocument.Parse(File.ReadAllText(meridianJsonPath));
        var root = document.RootElement;
        root.GetProperty("project").GetString().Should().Be("ExistingProject");
        root.GetProperty("codeMeridianUrl").GetString().Should().Be("http://existing");
        root.GetProperty("version").GetInt32().Should().Be(CodeMeridianConfigFileStore.CurrentConfigVersion);
        root.GetProperty("configurationFiles")
            .EnumerateArray()
            .Select(element => element.GetString())
            .Should()
            .Contain([".env", "custom.settings.json", "appsettings.json"]);
        root.GetProperty("configurationFiles")
            .EnumerateArray()
            .Count(element => string.Equals(element.GetString(), ".env", StringComparison.Ordinal))
            .Should()
            .Be(1);
        root.GetProperty("indexing")
            .GetProperty("fileRoles")
            .TryGetProperty("generated", out _)
            .Should()
            .BeTrue();
    }

    [Fact]
    public void WriteGlobal_RefreshesExistingFileWithGlobalDefaults()
    {
        var globalRoot = Directory.CreateDirectory(Path.Combine(_root.FullName, "global"));
        File.WriteAllText(Path.Combine(globalRoot.FullName, "meridian.json"), """
            {
              "project": "ShouldBeCleared",
              "codeMeridianUrl": "http://old",
              "useGlobalCache": false
            }
            """);

        var sut = new CodeMeridianConfigFileStore();

        var result = sut.WriteGlobal("http://global:5100", overwrite: false, globalRoot);

        result.Created.Should().BeFalse();
        result.Changed.Should().BeTrue();
        result.BackupPath.Should().NotBeNull();
        File.Exists(Path.Combine(globalRoot.FullName, "meridian.schema.json")).Should().BeTrue();
        File.Exists(Path.Combine(globalRoot.FullName, ".meridian", "architecture.json")).Should().BeTrue();
        File.Exists(Path.Combine(globalRoot.FullName, ".meridian", "keyword-classification.json")).Should().BeTrue();
        File.Exists(Path.Combine(globalRoot.FullName, ".meridian", "database-tracing.json")).Should().BeTrue();

        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(globalRoot.FullName, "meridian.json")));
        var root = document.RootElement;
        root.GetProperty("project").GetString().Should().BeEmpty();
        root.GetProperty("codeMeridianUrl").GetString().Should().Be("http://global:5100");
        root.GetProperty("useGlobalCache").GetBoolean().Should().BeTrue();
        root.GetProperty("version").GetInt32().Should().Be(CodeMeridianConfigFileStore.CurrentConfigVersion);
    }

    [Fact]
    public void WriteGlobal_OverwriteReplacesInvalidExistingFile()
    {
        var globalRoot = Directory.CreateDirectory(Path.Combine(_root.FullName, "overwrite"));
        var meridianJsonPath = Path.Combine(globalRoot.FullName, "meridian.json");
        File.WriteAllText(meridianJsonPath, "{ invalid json");

        var sut = new CodeMeridianConfigFileStore();

        var result = sut.WriteGlobal("http://global:5100", overwrite: true, globalRoot);

        result.Created.Should().BeFalse();
        result.Changed.Should().BeTrue();
        result.PreviousVersion.Should().Be(0);
        result.BackupPath.Should().NotBeNull();
        File.Exists(result.BackupPath!).Should().BeTrue();

        using var document = JsonDocument.Parse(File.ReadAllText(meridianJsonPath));
        var root = document.RootElement;
        root.GetProperty("project").GetString().Should().BeEmpty();
        root.GetProperty("codeMeridianUrl").GetString().Should().Be("http://global:5100");
        root.GetProperty("useGlobalCache").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void WriteAgentCapabilities_RespectsOverwriteFlag()
    {
        var sut = new CodeMeridianConfigFileStore();

        sut.WriteAgentCapabilities(_root, overwrite: false);
        var targetPath = Path.Combine(_root.FullName, CodeMeridianConfigFileStore.DefaultAgentCapabilitiesDirectory, "agent-capabilities.md");
        var originalContent = File.ReadAllText(targetPath);
        File.WriteAllText(targetPath, "custom content");

        sut.WriteAgentCapabilities(_root, overwrite: false);
        File.ReadAllText(targetPath).Should().Be("custom content");

        sut.WriteAgentCapabilities(_root, overwrite: true);
        File.ReadAllText(targetPath).Should().Be(originalContent);
    }

    public void Dispose()
    {
        if (!Directory.Exists(_root.FullName))
            return;

        try
        {
            Directory.Delete(_root.FullName, recursive: true);
        }
        catch (IOException)
        {
            // Temporary test artifacts can remain briefly locked on Windows.
        }
    }
}
