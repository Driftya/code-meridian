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
        json.Should().Contain("Enabled by default so repo-controlled build and lint diagnostics can run on trusted repos.");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
