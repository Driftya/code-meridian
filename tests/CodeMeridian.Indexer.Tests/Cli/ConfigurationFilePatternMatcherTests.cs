using CodeMeridian.Indexer.Cli.Configuration;
using FluentAssertions;

namespace CodeMeridian.Indexer.Tests.Cli;

public sealed class ConfigurationFilePatternMatcherTests
{
    [Fact]
    public void IsConfigurationFile_WithWildcardPattern_MatchesCaseInsensitively()
    {
        var file = new FileInfo(@"C:\repo\appsettings.Development.json");

        var result = ConfigurationFilePatternMatcher.IsConfigurationFile(file, ["appsettings.*.json"]);

        result.Should().BeTrue();
    }

    [Fact]
    public void IsConfigurationFile_WithCustomPatterns_ExcludesDefaultMatchesNotConfigured()
    {
        var file = new FileInfo(@"C:\repo\docker-compose.yml");

        var result = ConfigurationFilePatternMatcher.IsConfigurationFile(file, [".env"]);

        result.Should().BeFalse();
    }

    [Fact]
    public void IsConfigurationFile_WithoutCustomPatterns_UsesDefaults()
    {
        var file = new FileInfo(@"C:\repo\.env");

        var result = ConfigurationFilePatternMatcher.IsConfigurationFile(file);

        result.Should().BeTrue();
    }
}
