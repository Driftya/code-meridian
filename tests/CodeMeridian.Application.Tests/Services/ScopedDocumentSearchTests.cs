using System.Text.Json;
using CodeMeridian.Application.Projects;
using CodeMeridian.Application.Services;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;
using CodeMeridian.Core.Projects;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace CodeMeridian.Application.Tests.Services;

public sealed class ScopedDocumentSearchTests
{
    private readonly IProjectProfileRepository profiles = Substitute.For<IProjectProfileRepository>();
    private readonly IVectorRepository vector = Substitute.For<IVectorRepository>();

    [Fact]
    public async Task ScopedSearchFiltersStorageAndRejectsEscapedCrossProjectAndRemovedResults()
    {
        Publish();
        vector.SearchByTextInScopeAsync("chapter", "Docs", Arg.Any<DocumentQueryScope>(), 8, Arg.Any<CancellationToken>()).Returns(
            [Document("current", "docs/a.md"), Document("outside", "other/a.md"), Document("prefix", "docs-old/a.md"),
             Document("case", "Docs/a.md"), Document("escape", "docs/../secret.md"), Document("deleted", "docs/deleted.md"),
             Document("foreign", "docs/a.md") with { ProjectContext = "Other" }, Document("unknown-project", "docs/a.md") with { ProjectContext = null }]);
        profiles.GetAsync("Docs", "docs/deleted.md", Arg.Any<CancellationToken>()).Returns(Stored());

        var result = await Service().SearchDocumentationAsync("chapter", "Docs", rootPath: "docs");

        result.Should().Contain("status: ok").And.Contain("Scope: docs.").And.Contain("current content");
        foreach (var id in new[] { "outside", "prefix", "case", "escape", "deleted", "foreign", "unknown-project" })
            result.Should().NotContain(id + " content");
        await vector.Received(1).SearchByTextInScopeAsync("chapter", "Docs",
            Arg.Is<DocumentQueryScope>(scope => scope.RootPath == "docs" && scope.TargetPath == null), 8, Arg.Any<CancellationToken>());
        await vector.DidNotReceiveWithAnyArgs().SearchByTextAsync(default!, default, default, default);
    }

    [Fact]
    public async Task ExactTargetReturnsChunksFromOnlyThatFile()
    {
        Publish();
        vector.SearchByTextInScopeAsync("chapter", "Docs", Arg.Any<DocumentQueryScope>(), 8, Arg.Any<CancellationToken>()).Returns(
            [Document("first", "docs/a.md"), Document("second", "docs/a.md"), Document("sibling", "docs/b.md")]);

        var result = await Service().SearchDocumentationAsync("chapter", "Docs", rootPath: "docs", targetPath: "docs\\a.md");

        result.Should().Contain("Found **2**").And.Contain("Scope: docs/a.md.").And.NotContain("sibling content");
    }

    [Fact]
    public async Task RootOnlyEvidenceNeverAuthorizesTheUnfilteredFallback()
    {
        Publish();

        var result = await Service().SearchDocumentationAsync("chapter", "Docs");

        result.Should().Contain("status: unknown");
        await vector.DidNotReceiveWithAnyArgs().SearchByTextAsync(default!, default, default, default);
        await vector.DidNotReceiveWithAnyArgs().SearchByTextInScopeAsync(default!, default!, default!, default, default);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GenerationChangesDiscardBothEmptyAndNonemptyScopedSearches(bool empty)
    {
        var stored = Publish();
        vector.SearchByTextInScopeAsync("chapter", "Docs", Arg.Any<DocumentQueryScope>(), 8, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            profiles.GetAsync("Docs", Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(stored with { LatestGeneration = 2 });
            return Task.FromResult<IReadOnlyList<KnowledgeDocument>>(empty ? [] : [Document("stale", "docs/a.md")]);
        });

        var result = await Service().SearchDocumentationAsync("chapter", "Docs", rootPath: "docs");

        result.Should().Contain("status: stale").And.NotContain("stale content").And.NotContain("No matching");
    }

    [Fact]
    public async Task ScopeCoverageIsRevalidatedEvenWhenGenerationDoesNotChange()
    {
        var stored = Publish();
        vector.SearchByTextInScopeAsync("chapter", "Docs", Arg.Any<DocumentQueryScope>(), 8, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            profiles.GetAsync("Docs", Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(stored with
            { AnalyzerOutcomes = [ScopedDocumentPolicyTests.Outcome("other")] });
            return Task.FromResult<IReadOnlyList<KnowledgeDocument>>([]);
        });

        (await Service().SearchDocumentationAsync("chapter", "Docs", rootPath: "docs"))
            .Should().Contain("status: stale").And.NotContain("No matching");
    }

    [Fact]
    public async Task ValidEmptySearchIsExplicitlyExecutedWithinItsScope()
    {
        Publish();
        (await Service().SearchDocumentationAsync("chapter", "Docs", rootPath: "docs"))
            .Should().Contain("status: ok").And.Contain("Scope: docs.").And.Contain("No matching documents returned");
    }

    [Fact]
    public async Task ScopedStorageFailureAndCancellationCannotBecomeEmptySuccess()
    {
        Publish();
        vector.SearchByTextInScopeAsync("chapter", "Docs", Arg.Any<DocumentQueryScope>(), 8, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<KnowledgeDocument>>(new IOException("private backend details")));
        (await Service().SearchDocumentationAsync("chapter", "Docs", rootPath: "docs"))
            .Should().Contain("status: failed").And.NotContain("private backend details").And.NotContain("No matching");
        vector.SearchByTextInScopeAsync("chapter", "Docs", Arg.Any<DocumentQueryScope>(), 8, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<KnowledgeDocument>>(new OperationCanceledException()));
        var action = () => Service().SearchDocumentationAsync("chapter", "Docs", rootPath: "docs");
        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [Theory]
    [InlineData(null, "docs", null)]
    [InlineData("Docs", "../docs", null)]
    [InlineData("Docs", "docs", "other/a.md")]
    public async Task InvalidScopeFailsBeforeReadingEvidenceOrStorage(string? project, string root, string? target)
    {
        var action = () => Service().SearchDocumentationAsync("chapter", project, rootPath: root, targetPath: target);
        await action.Should().ThrowAsync<ArgumentException>();
        await profiles.DidNotReceiveWithAnyArgs().GetAsync(default!, default, default);
        await vector.DidNotReceiveWithAnyArgs().SearchByTextInScopeAsync(default!, default!, default!, default, default);
    }

    [Fact]
    public async Task ExactDocumentWorkflowUsesScopedStorageAndRootCoverage()
    {
        Publish();
        var service = Service();
        using var plan = JsonDocument.Parse(await service.PlanContextWorkflowAsync("Review chapter", "docs/a.md", "Docs"));
        plan.RootElement.GetProperty("status").GetString().Should().Be("valid");
        plan.RootElement.GetProperty("steps")[1].GetProperty("inputHints").GetProperty("targetPath").GetString().Should().Be("docs/a.md");

        using var result = JsonDocument.Parse(await service.ExecuteContextWorkflowAsync("Review chapter", "docs/a.md", "Docs"));

        result.RootElement.GetProperty("status").GetString().Should().Be("completed");
        await vector.Received(1).SearchByTextInScopeAsync("docs/a.md", "Docs",
            Arg.Is<DocumentQueryScope>(scope => scope.TargetPath == "docs/a.md"), 8, Arg.Any<CancellationToken>());
        await vector.DidNotReceiveWithAnyArgs().SearchByTextAsync(default!, default, default, default);
    }

    private StoredProjectProfile Publish()
    {
        var stored = Stored();
        profiles.GetAsync("Docs", Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(stored);
        profiles.GetAsync("Docs", "docs/a.md", Arg.Any<CancellationToken>()).Returns(stored with { Target = new("docs/a.md", "markdown") });
        return stored;
    }

    private static StoredProjectProfile Stored()
    {
        var profile = ScopedDocumentPolicyTests.Profile();
        return new(1, 1, DateTimeOffset.UtcNow, profile.Profile!, null) { AnalyzerOutcomes = profile.AnalyzerOutcomes };
    }

    private static KnowledgeDocument Document(string id, string path) => new()
        { Id = id, Source = path, Content = id + " content", ProjectContext = "Docs" };

    private CodebaseQueryService Service() => new(Substitute.For<ICodeGraphRepository>(), vector, new NoOpEmbeddingProvider(),
        Options.Create(new CodebaseAnalysisOptions()), Options.Create(new CodebaseIndexingOptions()), new DefaultAnalysisProfilePolicy(),
        new StaticProjectAnalysisOptionsResolver(new CodebaseAnalysisOptions()), new ProjectProfileService(profiles));
}
