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

    [Fact]
    public void IsConfigurationFile_IgnoresBlankPatterns()
    {
        var file = new FileInfo(@"C:\repo\appsettings.json");

        var result = ConfigurationFilePatternMatcher.IsConfigurationFile(file, [" ", "\t"]);

        result.Should().BeFalse();
    }

    [Fact]
    public void IsConfigurationFile_WithWildcardNotAnchoredAtStart_MatchesSuffixPattern()
    {
        var file = new FileInfo(@"C:\repo\docker-compose.sample.yaml");

        var result = ConfigurationFilePatternMatcher.IsConfigurationFile(file, ["*sample.yaml"]);

        result.Should().BeTrue();
    }

    [Theory]
    [InlineData(@"C:\repo\appsettings.json", 0)]
    [InlineData(@"C:\repo\appsettings.Development.json", 1)]
    [InlineData(@"C:\repo\meridian.json", 2)]
    [InlineData(@"C:\repo\meridian.sample.json", 2)]
    [InlineData(@"C:\repo\custom.json", 3)]
    [InlineData(@"C:\repo\.env", 4)]
    [InlineData(@"C:\repo\docker-compose.yml", 5)]
    public void GetOrder_ReturnsExpectedPriority(string path, int expectedOrder)
    {
        var result = ConfigurationFilePatternMatcher.GetOrder(new FileInfo(path));

        result.Should().Be(expectedOrder);
    }
}
