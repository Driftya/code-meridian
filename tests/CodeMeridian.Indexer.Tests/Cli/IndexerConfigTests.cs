using CodeMeridian.Indexer.Cli;
using FluentAssertions;

namespace CodeMeridian.Indexer.Tests.Cli;

public sealed class IndexerConfigTests : IDisposable
{
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
              "allowRepoScripts": true
            }
            """);

        var result = IndexerConfig.Load(new DirectoryInfo(_root));

        result.Should().NotBeNull();
        result!.Project.Should().Be("MyApi");
        result.CodeMeridianUrl.Should().Be("http://localhost:5100");
        result.AllowRepoScripts.Should().BeTrue();
    }

    [Fact]
    public void Load_SupportsUrlAlias()
    {
        File.WriteAllText(
            Path.Combine(_root, "meridian.json"),
            """
            {
              "project": "MyApi",
              "url": "http://192.168.1.10:5100"
            }
            """);

        var result = IndexerConfig.Load(new DirectoryInfo(_root));

        result.Should().NotBeNull();
        result!.CodeMeridianUrl.Should().Be("http://192.168.1.10:5100");
    }

    [Fact]
    public void Load_FallsBackToGlobalConfigWhenLocalConfigIsMissing()
    {
        var globalRoot = Directory.CreateDirectory(Path.Combine(_root, "global"));
        File.WriteAllText(
            Path.Combine(globalRoot.FullName, "meridian.json"),
            """
            {
              "project": "ShouldBeIgnored",
              "codeMeridianUrl": "http://global:5100",
              "allowRepoScripts": true
            }
            """);

        var result = IndexerConfig.Load(new DirectoryInfo(Path.Combine(_root, "project")), globalRoot);

        result.Should().NotBeNull();
        result!.Project.Should().BeNull();
        result.CodeMeridianUrl.Should().Be("http://global:5100");
        result.AllowRepoScripts.Should().BeTrue();
    }

    [Fact]
    public void Load_PrefersLocalConfigOverGlobalConfig()
    {
        var globalRoot = Directory.CreateDirectory(Path.Combine(_root, "global"));
        File.WriteAllText(
            Path.Combine(globalRoot.FullName, "meridian.json"),
            """
            {
              "codeMeridianUrl": "http://global:5100"
            }
            """);
        File.WriteAllText(
            Path.Combine(_root, "meridian.json"),
            """
            {
              "project": "LocalApi",
              "codeMeridianUrl": "http://local:5100"
            }
            """);

        var result = IndexerConfig.Load(new DirectoryInfo(_root), globalRoot);

        result.Should().NotBeNull();
        result!.Project.Should().Be("LocalApi");
        result.CodeMeridianUrl.Should().Be("http://local:5100");
    }

    [Fact]
    public void Write_CreatesMeridianJson()
    {
        IndexerConfig.Write(new DirectoryInfo(_root), "MyApi", "http://localhost:5100", overwrite: false);

        var json = File.ReadAllText(Path.Combine(_root, "meridian.json"));

        json.Should().Contain("\"project\": \"MyApi\"");
        json.Should().Contain("\"$schema\": \"./meridian.schema.json\"");
        json.Should().Contain("\"codeMeridianUrl\": \"http://localhost:5100\"");
        json.Should().Contain("\"allowRepoScripts\": true");
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

        IndexerConfig.WriteGlobal("http://global:5100", overwrite: false, globalRoot);

        var json = File.ReadAllText(Path.Combine(globalRoot.FullName, "meridian.json"));
        json.Should().Contain("\"project\": \"\"");
        json.Should().Contain("\"codeMeridianUrl\": \"http://global:5100\"");
        File.Exists(Path.Combine(globalRoot.FullName, "meridian.schema.json")).Should().BeTrue();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
