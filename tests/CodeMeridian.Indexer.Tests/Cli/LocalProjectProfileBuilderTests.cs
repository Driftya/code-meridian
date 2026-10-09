using CodeMeridian.Indexer.Cli.Commands;
using CodeMeridian.Tooling.Configuration;
using CodeMeridian.Tooling.Discovery;
using CodeMeridian.Tooling.Storage;
using FluentAssertions;

namespace CodeMeridian.Indexer.Tests.Cli;

public sealed class LocalProjectProfileBuilderTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateDirectory(Path.Combine(
        Path.GetTempPath(), $"codemeridian-profile-{Guid.NewGuid():N}"));
    private readonly LocalProjectProfileBuilder _sut = new(new RepositoryInventoryDiscovery());

    [Theory]
    [InlineData("App.cs", "csharp")]
    [InlineData("app.ts", "typescript")]
    [InlineData("app.jsx", "javascript")]
    [InlineData("chapter.md", "markdown")]
    [InlineData("notes.txt", "text")]
    [InlineData("schema.sql", "sql")]
    [InlineData("Publish.ps1", "powershell")]
    [InlineData("Module.psm1", "powershell")]
    [InlineData("Module.psd1", "powershell")]
    [InlineData("types.d.ts", "typescript_declaration")]
    [InlineData("page.html", "html")]
    [InlineData("style.css", "css")]
    [InlineData("style.scss", "scss")]
    [InlineData("cover.png", "asset")]
    [InlineData("appsettings.json", "configuration")]
    [InlineData("app.py", "other")]
    public void Build_ObservesFileKindsWithoutRequiringAnAnalyzer(string path, string expectedKind)
    {
        Write(path);

        var profile = _sut.Build(Settings());

        profile.FileKinds.Should().ContainSingle().Which.Kind.Should().Be(expectedKind);
        profile.FileCount.Should().Be(1);
        profile.DiscoveryState.Should().Be("complete");
        profile.EvidenceSource.Should().Be("local_discovery");
        profile.Analyzers.Should().OnlyContain(analyzer => analyzer.IndexingState == "unknown");
    }

    [Fact]
    public void Build_MixedMarkdownAndPowerShellDoesNotClaimCodeReadiness()
    {
        Write("manuscript/001-chapter.md");
        Write("scripts/Publish.ps1", "throw 'Do not execute this script'");

        var profile = _sut.Build(Settings());

        profile.FileKinds.Select(kind => kind.Kind).Should().Equal("markdown", "powershell");
        profile.Analyzers.Single(analyzer => analyzer.Id == "powershell").Supported.Should().BeFalse();
        profile.Analyzers.Single(analyzer => analyzer.Id == "powershell").SupportedCapabilities.Should().BeEmpty();
        profile.AnalysisRoots.Should().NotContain(root => root.Kind == "csharp" || root.Kind == "typescript");
    }

    [Fact]
    public void Build_SkipFlagsChangeConfigurationWithoutChangingObservedFiles()
    {
        Write("App.cs");
        Write("web/index.ts");
        Write("README.md");
        Write("settings.json");
        Write("query.sql");

        var enabled = _sut.Build(Settings(sql: new CodeMeridianSqlOptions { Enabled = true }));
        var disabled = _sut.Build(Settings(skip: true, sql: new CodeMeridianSqlOptions { Enabled = true }));

        disabled.FileKinds.Should().BeEquivalentTo(enabled.FileKinds);
        enabled.Analyzers.Where(analyzer => analyzer.Supported)
            .Should().OnlyContain(analyzer => analyzer.ConfigurationState == "enabled");
        disabled.Analyzers.Where(analyzer => analyzer.Supported)
            .Should().OnlyContain(analyzer => analyzer.ConfigurationState == "disabled");
    }

    [Fact]
    public void Build_ReportsSqlFilesWhenSqlIsDisabled()
    {
        Write("migrations/001.sql");

        var profile = _sut.Build(Settings());

        profile.FileKinds.Should().ContainSingle(kind => kind.Kind == "sql");
        profile.Analyzers.Single(analyzer => analyzer.Id == "sql").ConfigurationState.Should().Be("disabled");
    }

    [Fact]
    public void Build_BoundsExamplesWithoutLosingCounts()
    {
        foreach (var number in Enumerable.Range(1, 20))
            Write($"manuscript/{number:000}.md");

        var profile = _sut.Build(Settings());
        var markdown = profile.FileKinds.Single();

        markdown.Count.Should().Be(20);
        markdown.ExamplePaths.Should().Equal(Enumerable.Range(1, 5).Select(number => $"manuscript/{number:000}.md"));
    }

    [Fact]
    public void Build_ObservesManifestRootsWithoutReadingOrEvaluatingManifests()
    {
        Write("backend/App.csproj", "not valid XML");
        Write("backend/App.cs");
        Write("web/tsconfig.json", "not valid JSON");
        Write("web/src/index.ts");

        var profile = _sut.Build(Settings());

        profile.AnalysisRoots.Should().Contain(root =>
            root.Kind == "csharp" && root.Path == "backend" && root.EvidencePath == "backend/App.csproj");
        profile.AnalysisRoots.Should().Contain(root =>
            root.Kind == "typescript" && root.Path == "web" && root.EvidencePath == "web/tsconfig.json");
    }

    [Fact]
    public void Build_TypeScriptWithoutManifestUsesRepositoryRoot()
    {
        Write("src/index.ts");

        var profile = _sut.Build(Settings());

        profile.AnalysisRoots.Should().ContainSingle(root => root.Kind == "typescript" && root.Path == ".");
    }

    [Fact]
    public void Build_EmptyRepositoryHasNoInventedRootsOrIndexingEvidence()
    {
        var profile = _sut.Build(Settings());

        profile.FileCount.Should().Be(0);
        profile.AnalysisRoots.Should().BeEmpty();
        profile.Analyzers.Should().OnlyContain(analyzer => analyzer.IndexingState == "unknown");
    }

    [Fact]
    public void Build_BoundsRootEvidenceWithoutLosingRootCount()
    {
        foreach (var number in Enumerable.Range(1, 60))
            Write($"projects/{number:000}/App.csproj");

        var profile = _sut.Build(Settings());

        profile.AnalysisRootCount.Should().Be(60);
        profile.AnalysisRoots.Should().HaveCount(50);
    }

    private ResolvedIndexerSettings Settings(bool skip = false, CodeMeridianSqlOptions? sql = null) => new()
    {
        RootPath = _root,
        Project = "Fixture",
        CodeMeridianUrl = "http://localhost:1",
        StorageMode = IndexerStorageMode.Repository,
        SkipCSharp = skip,
        SkipTypeScript = skip,
        IncludeDocs = !skip,
        SkipConfiguration = skip,
        SkipSql = skip,
        Sql = sql
    };

    private void Write(string path, string content = "")
    {
        var fullPath = Path.Combine(_root.FullName, path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }

    public void Dispose() => _root.Delete(recursive: true);
}
