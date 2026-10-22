using System.Text.Json;
using CodeMeridian.Indexer.Cli.Commands;
using CodeMeridian.Tooling.Configuration;
using CodeMeridian.Tooling.Discovery;
using CodeMeridian.Tooling.Storage;
using FluentAssertions;

namespace CodeMeridian.Indexer.Tests.Cli;

public sealed class ProjectAnalysisFingerprintTests
{
    [Theory]
    [InlineData("csharp")]
    [InlineData("documents")]
    [InlineData("configuration")]
    public void ManagedFingerprintsDescribeLoadedModulesWithoutClaimingCoverage(string analyzer)
    {
        var fingerprint = ProjectAnalysisFingerprints.Analyzer(analyzer);

        fingerprint.Should().NotBeNull();
        fingerprint!.Validate().Should().BeNull();
        ProjectAnalysisFingerprints.Analyzer(analyzer).Should().Be(fingerprint);
        LocalProjectProfileBuilder.BuildAnalyzers(Settings()).Single(item => item.Id == analyzer)
            .IndexingState.Should().Be("unknown");
    }

    [Theory]
    [InlineData("typescript")]
    [InlineData("frontend")]
    [InlineData("sql")]
    [InlineData("powershell")]
    public void LocalDiscoveryDoesNotInventWorkerFingerprints(string analyzer) =>
        ProjectAnalysisFingerprints.Analyzer(analyzer).Should().BeNull();

    [Fact]
    public void ConfigurationHashExcludesCredentialsTransportAndRunSelection()
    {
        var original = Settings();
        var changed = Settings("{\"apiKey\":\"secret\",\"codeMeridianUrl\":\"https://other-server\",\"clear\":true,\"incremental\":false,\"watch\":true,\"dryRun\":true}");

        ProjectAnalysisFingerprints.Configuration(original).Should().Be(ProjectAnalysisFingerprints.Configuration(changed));
    }

    [Theory]
    [InlineData("{\"includeDocs\":false}")]
    [InlineData("{\"skipCSharp\":true}")]
    [InlineData("{\"allowRepoScripts\":true}")]
    [InlineData("{\"configurationFiles\":[\"settings.json\"]}")]
    [InlineData("{\"architecturePath\":\"rules.json\"}")]
    [InlineData("{\"sql\":{\"enabled\":true,\"databaseScope\":\"custom\"}}")]
    public void ChangedResolvedAnalysisSettingsInvalidateConfigurationHash(string changes) =>
        ProjectAnalysisFingerprints.Configuration(Settings(changes)).Should().NotBe(ProjectAnalysisFingerprints.Configuration(Settings()));

    [Fact]
    public void ProducerBindingOrderDoesNotInvalidateEquivalentConfiguration()
    {
        var first = Settings("{\"packageProducerBindings\":{\"A\":\"One\",\"B\":\"Two\"}}");
        var reordered = Settings("{\"packageProducerBindings\":{\"B\":\"Two\",\"A\":\"One\"}}");

        ProjectAnalysisFingerprints.Configuration(first).Should().Be(ProjectAnalysisFingerprints.Configuration(reordered));
    }

    private static ResolvedIndexerSettings Settings(string changes = "{}")
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        using var document = JsonDocument.Parse(changes);
        var values = document.RootElement;
        bool Flag(string name, bool fallback = false) => values.TryGetProperty(name, out var value) ? value.GetBoolean() : fallback;
        return new ResolvedIndexerSettings
        {
            RootPath = new DirectoryInfo(Path.GetTempPath()), Project = "Fixture", StorageMode = IndexerStorageMode.Repository,
            CodeMeridianUrl = values.TryGetProperty("codeMeridianUrl", out var url) ? url.GetString()! : "http://localhost",
            ApiKey = values.TryGetProperty("apiKey", out var key) ? key.GetString() : null,
            Clear = Flag("clear"), Incremental = Flag("incremental", true), Watch = Flag("watch"), DryRun = Flag("dryRun"),
            IncludeDocs = Flag("includeDocs", true), SkipCSharp = Flag("skipCSharp"), AllowRepoScripts = Flag("allowRepoScripts"),
            ConfigurationFiles = values.TryGetProperty("configurationFiles", out var files) ? files.Deserialize<string[]>(options) : null,
            ArchitecturePath = values.TryGetProperty("architecturePath", out var architecture) ? architecture.GetString() : null,
            Sql = values.TryGetProperty("sql", out var sql) ? sql.Deserialize<CodeMeridianSqlOptions>(options) : null,
            PackageProducerBindings = values.TryGetProperty("packageProducerBindings", out var bindings)
                ? bindings.Deserialize<Dictionary<string, string>>(options) : null
        };
    }
}
