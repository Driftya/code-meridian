using CodeMeridian.Application.Projects;
using CodeMeridian.Application.Services;
using CodeMeridian.Application.Services.ContextWorkflows;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;
using CodeMeridian.Core.Projects;
using CodeMeridian.Indexer.Cli.Commands;
using CodeMeridian.Tooling.Discovery;
using CodeMeridian.Tooling.Storage;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace CodeMeridian.Indexer.Tests.Cli;

public sealed class ProjectCapabilityMatrixTests : IDisposable
{
    private readonly DirectoryInfo root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "capability-matrix-" + Guid.NewGuid().ToString("N")));

    [Theory]
    [InlineData("csharp", "unknown", "not_applicable")]
    [InlineData("typescript-no-config", "unknown", "not_applicable")]
    [InlineData("markdown", "not_applicable", "ready")]
    [InlineData("markdown-powershell", "not_applicable", "ready")]
    [InlineData("sql-disabled", "not_applicable", "not_applicable")]
    [InlineData("sql-enabled", "not_applicable", "not_applicable")]
    [InlineData("mixed-roots", "unknown", "ready")]
    [InlineData("unsupported", "not_applicable", "not_applicable")]
    [InlineData("empty", "not_applicable", "not_applicable")]
    [InlineData("failed", "failed", "not_applicable")]
    [InlineData("partial", "partial", "not_applicable")]
    [InlineData("skipped", "disabled", "not_applicable")]
    [InlineData("stale", "stale", "stale")]
    public async Task PublishedMatrixEvidenceControlsBothPlanningAndDirectQueries(string scenario, string callable, string documents)
    {
        var paths = scenario switch
        {
            "csharp" or "failed" or "partial" or "skipped" or "stale" => new[] { "Main.cs" },
            "typescript-no-config" => ["index.ts"],
            "markdown" => ["chapter.md"],
            "markdown-powershell" => ["chapter.md", "main.ps1"],
            "sql-disabled" or "sql-enabled" => ["schema.sql"],
            "mixed-roots" => ["api/Main.cs", "web/index.ts", "web/tsconfig.json", "docs/chapter.md", "web/index.html", "web/style.css"],
            "unsupported" => ["main.py"],
            _ => []
        };
        foreach (var path in paths)
        {
            var absolute = Path.Combine(root.FullName, path);
            Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
            File.WriteAllText(absolute, "");
        }
        var snapshot = new LocalProjectProfileBuilder(new RepositoryInventoryDiscovery()).BuildSnapshot(new()
        {
            RootPath = root, Project = "Matrix", CodeMeridianUrl = "http://localhost", StorageMode = IndexerStorageMode.Repository,
            IncludeDocs = true, SkipConfiguration = true, SkipCSharp = scenario == "skipped",
            Sql = new() { Enabled = scenario == "sql-enabled" }, PowerShell = new() { Enabled = scenario == "markdown-powershell" }
        }, 1);
        var hash = AnalyzedSourceSnapshot.Capture(root, snapshot.Files, CancellationToken.None).Fingerprint;
        var profile = snapshot.Profile with { SourceFingerprint = hash };
        var outcomes = profile.Analyzers.Where(analyzer => analyzer.ConfigurationState == "enabled")
            .Select(analyzer => new ProjectAnalyzerOutcome(analyzer.Id, ".",
                scenario == "failed" && analyzer.Id == "csharp" ? "failed" : scenario == "partial" && analyzer.Id == "csharp" ? "partial" : "succeeded",
                analyzer.Id == "documents" ? "complete" : scenario == "partial" && analyzer.Id == "csharp" ? "partial" : "unknown",
                "full", DateTimeOffset.UtcNow, "matrix/1", []) { Fingerprint = analyzer.Fingerprint, SourceFingerprint = hash }).ToArray();
        var profiles = Substitute.For<IProjectProfileRepository>();
        profiles.GetAsync("Matrix", Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(new StoredProjectProfile(1,
            scenario == "stale" ? 2 : 1, DateTimeOffset.UtcNow, profile, null) { AnalyzerOutcomes = outcomes });
        var profileService = new ProjectProfileService(profiles);
        var published = await profileService.GetAsync("Matrix");
        ContextWorkflowToolPolicy.Evaluate("find_implementation_surface", published).Status.Should().Be(callable);
        ContextWorkflowToolPolicy.Evaluate("search_documentation", published).Status.Should().Be(documents);
        var graph = Substitute.For<ICodeGraphRepository>();
        var vector = Substitute.For<IVectorRepository>();
        var resolver = Substitute.For<IProjectAnalysisOptionsResolver>();
        var service = new CodebaseQueryService(graph, vector, new NoOpEmbeddingProvider(), Options.Create(new CodebaseAnalysisOptions()),
            Options.Create(new CodebaseIndexingOptions()), new DefaultAnalysisProfilePolicy(), resolver, profileService);

        (await service.FindImplementationSurfaceAsync("Find implementation", projectContext: "Matrix")).Should().Contain("status: " + callable);
        var search = await service.SearchDocumentationAsync("Chapter", "Matrix");
        search.Should().Contain("status: " + (documents == "ready" ? "ok" : documents));
        await graph.DidNotReceiveWithAnyArgs().QueryNodesAsync(default!, default);
        if (documents == "ready") await vector.Received(1).SearchByTextAsync("Chapter", "Matrix", 8, Arg.Any<CancellationToken>());
        else await vector.DidNotReceiveWithAnyArgs().SearchByTextAsync(default!, default, default, default);
    }

    [Fact]
    public void IncompleteDiscoveryCannotAuthorizeEvenAnApparentlyEmptyRepository()
    {
        var profile = new LocalProjectProfileBuilder(new RepositoryInventoryDiscovery()).Build(new()
        { RootPath = root, Project = "Matrix", CodeMeridianUrl = "http://localhost", StorageMode = IndexerStorageMode.Repository });
        var result = new ProjectProfileResult("available", "Matrix", 1, 1, DateTimeOffset.UtcNow,
            profile with { DiscoveryState = "partial", Warnings = ["Directory could not be enumerated."], WarningCount = 1 }, null, []);
        ContextWorkflowToolPolicy.Evaluate("find_implementation_surface", result).Status.Should().Be("unknown");
        ContextWorkflowToolPolicy.Evaluate("search_documentation", result).Status.Should().Be("unknown");
    }

    public void Dispose() => root.Delete(recursive: true);
}
