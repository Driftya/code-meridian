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
              "useGlobalCache": true
            }
            """);

        var result = _store.LoadLocal(new DirectoryInfo(_root));

        result.Should().NotBeNull();
        result!.Project.Should().Be("MyApi");
        result.CodeMeridianUrl.Should().Be("http://localhost:5100");
        result.AllowRepoScripts.Should().BeTrue();
        result.UseGlobalCache.Should().BeTrue();
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
    }

    [Fact]
    public void Write_CreatesMeridianJson()
    {
        _store.Write(new DirectoryInfo(_root), "MyApi", "http://localhost:5100", overwrite: false);

        var json = File.ReadAllText(Path.Combine(_root, "meridian.json"));

        json.Should().Contain("\"project\": \"MyApi\"");
        json.Should().Contain("\"$schema\": \"./meridian.schema.json\"");
        json.Should().Contain("\"codeMeridianUrl\": \"http://localhost:5100\"");
        json.Should().Contain("\"allowRepoScripts\": true");
        json.Should().Contain("\"useGlobalCache\": false");
        json.Should().Contain("\"analysis\"");
        json.Should().Contain("\"skipHeuristicSourcePrefixes\"");
        json.Should().Contain("\"preferProductionOverTests\": true");
        json.Should().Contain("\"DependencyInjection\"");
        json.Should().Contain("\"Startup\"");
        json.Should().Contain("\"CompositionRoot\"");
    }

    [Fact]
    public void WriteGlobal_CreatesGlobalMeridianJsonWithoutProjectName()
    {
        var globalRoot = Directory.CreateDirectory(Path.Combine(_root, "global"));

        _store.WriteGlobal("http://global:5100", overwrite: false, globalRoot);

        var json = File.ReadAllText(Path.Combine(globalRoot.FullName, "meridian.json"));
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
        json.Should().Contain("\"project\": \"\"");
        json.Should().Contain("\"codeMeridianUrl\": \"http://new:5100\"");
        json.Should().Contain("\"useGlobalCache\": true");
        json.Should().Contain("\"allowRepoScripts\": true");
        json.Should().Contain("\"customSetting\": \"keep-me\"");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
