using CodeMeridian.McpServer.Configuration;
using CodeMeridian.Tooling.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace CodeMeridian.McpServer.Tests;

public sealed class GlobalMeridianAnalysisConfigurationSourceTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "codemeridian-global-analysis-tests",
        Guid.NewGuid().ToString("N"));

    public GlobalMeridianAnalysisConfigurationSourceTests()
    {
        Directory.CreateDirectory(root);
    }

    [Fact]
    public async Task LoadAsync_ReadsGlobalAnalysisEntriesFromConfiguredHome()
    {
        Environment.SetEnvironmentVariable("CODEMERIDIAN_CONFIG_HOME", root);
        File.WriteAllText(
            Path.Combine(root, "meridian.json"),
            """
            {
              "analysis": {
                "ranking": {
                  "productionOnlyByDefault": false
                },
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

        var sut = new GlobalMeridianAnalysisConfigurationSource(
            new CodeMeridianConfigFileStore(),
            NullLogger<GlobalMeridianAnalysisConfigurationSource>.Instance);

        var result = await sut.LoadAsync();

        result.Entries.Should().Contain(entry =>
            entry.CanonicalKey == "analysis:ranking:productionOnlyByDefault" &&
            entry.Value == "false");
        result.Entries.Should().Contain(entry =>
            entry.CanonicalKey == "analysis:responsibilitySlices:namespaceRootOverrides:0:matchPrefix" &&
            entry.Value == "CodeMeridian.Application");
        result.Entries.Should().Contain(entry =>
            entry.CanonicalKey == "analysis:responsibilitySlices:namespaceRootOverrides:0:replaceWith" &&
            entry.Value == "CodeMeridian.Features");
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadAsync_WhenJsonIsInvalid_ReturnsWarning()
    {
        Environment.SetEnvironmentVariable("CODEMERIDIAN_CONFIG_HOME", root);
        File.WriteAllText(Path.Combine(root, "meridian.json"), "{ invalid json");

        var sut = new GlobalMeridianAnalysisConfigurationSource(
            new CodeMeridianConfigFileStore(),
            NullLogger<GlobalMeridianAnalysisConfigurationSource>.Instance);

        var result = await sut.LoadAsync();

        result.Entries.Should().BeEmpty();
        result.Warnings.Should().ContainSingle();
        result.Warnings[0].Should().Contain("Failed to load global `meridian.json` analysis");
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("CODEMERIDIAN_CONFIG_HOME", null);
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }
}
