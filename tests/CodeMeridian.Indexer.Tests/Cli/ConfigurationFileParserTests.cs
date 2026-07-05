using CodeMeridian.Indexer.Cli.Configuration;
using FluentAssertions;

namespace CodeMeridian.Indexer.Tests.Cli;

public sealed class ConfigurationFileParserTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "codemeridian-config-parser-tests",
        Guid.NewGuid().ToString("N"));

    public ConfigurationFileParserTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void ParseJson_FlattensNestedPaths()
    {
        var file = WriteFile(
            "appsettings.json",
            """
            {
              "Neo4j": {
                "Uri": "bolt://localhost:7687"
              }
            }
            """);

        var entries = ConfigurationFileParser.Parse(file, _root);

        entries.Should().ContainSingle(entry =>
            entry.CanonicalKey == "Neo4j:Uri" &&
            entry.RawKey == "Neo4j:Uri" &&
            entry.ValuePreview == "bolt://localhost:7687");
    }

    [Fact]
    public void ParseEnv_NormalizesDoubleUnderscoreAndMasksSecrets()
    {
        var file = WriteFile(
            ".env",
            """
            CodeMeridian__Auth__ApiKey=super-secret-value
            """);

        var entries = ConfigurationFileParser.Parse(file, _root);

        entries.Should().ContainSingle(entry =>
            entry.CanonicalKey == "CodeMeridian:Auth:ApiKey" &&
            entry.ValuePreview == "***" &&
            entry.IsSecretLike);
    }

    [Fact]
    public void ParseYaml_ExtractsEnvironmentMappingsAsCanonicalKeys()
    {
        var file = WriteFile(
            "docker-compose.yml",
            """
            services:
              api:
                environment:
                  Neo4j__Uri: bolt://neo4j:7687
            """);

        var entries = ConfigurationFileParser.Parse(file, _root);

        entries.Should().ContainSingle(entry =>
            entry.CanonicalKey == "Neo4j:Uri" &&
            entry.SourceKind == "yaml-environment" &&
            entry.ValuePreview == "bolt://neo4j:7687");
    }

    [Fact]
    public void ParseYaml_WithComplexKey_DoesNotThrowAndStillParsesScalarEntries()
    {
        var file = WriteFile(
            "complex.yaml",
            """
            ? { nested: key }
            : ignored
            services:
              api:
                environment:
                  Neo4j__Uri: bolt://neo4j:7687
            """);

        var act = () => ConfigurationFileParser.Parse(file, _root);

        var entries = act.Should().NotThrow().Subject;
        entries.Should().Contain(entry => entry.CanonicalKey == "Neo4j:Uri");
    }

    [Fact]
    public void ParseJson_FlattensNestedAnalysisObjectArrays()
    {
        var file = WriteFile(
            "meridian.json",
            """
            {
              "analysis": {
                "responsibilitySlices": {
                  "namespaceRootOverrides": [
                    {
                      "matchPrefix": "CodeMeridian.Application",
                      "replaceWith": "CodeMeridian.Features"
                    }
                  ]
                }
              }
            }
            """);

        var entries = ConfigurationFileParser.Parse(file, _root);

        entries.Should().Contain(entry =>
            entry.CanonicalKey == "analysis:responsibilitySlices:namespaceRootOverrides:0:matchPrefix" &&
            entry.ValuePreview == "CodeMeridian.Application");
        entries.Should().Contain(entry =>
            entry.CanonicalKey == "analysis:responsibilitySlices:namespaceRootOverrides:0:replaceWith" &&
            entry.ValuePreview == "CodeMeridian.Features");
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
