using CodeMeridian.Indexer.Cli.Configuration;
using FluentAssertions;

namespace CodeMeridian.Indexer.Tests.Cli;

public sealed class ConfigurationFileParserFailureTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "codemeridian-config-parser-failure-tests",
        Guid.NewGuid().ToString("N"));

    public ConfigurationFileParserFailureTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void TryParse_WithTemplatedMeridianSample_ReturnsFalseInsteadOfThrowing()
    {
        var file = WriteFile(
            "meridian.sample.json",
            """
            {
              "project": "{{project}}",
              "useGlobalCache": {{useGlobalCache}}
            }
            """);

        var result = ConfigurationFileParser.TryParse(file, _root, out var entries, out var error);

        result.Should().BeFalse();
        entries.Should().BeEmpty();
        error.Should().NotBeNullOrWhiteSpace();
    }

    private FileInfo WriteFile(string relativePath, string content)
    {
        var file = new FileInfo(Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        file.Directory!.Create();
        File.WriteAllText(file.FullName, content);
        return file;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
