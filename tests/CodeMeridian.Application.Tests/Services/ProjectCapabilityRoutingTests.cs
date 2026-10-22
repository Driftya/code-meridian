using System.Text.Json;
using CodeMeridian.Application.Projects;
using CodeMeridian.Application.Services;
using CodeMeridian.Application.Services.ContextWorkflows;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;
using CodeMeridian.Core.Projects;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace CodeMeridian.Application.Tests.Services;

public sealed class ProjectCapabilityRoutingTests
{
    [Fact]
    public void DocumentOnlyRepositoryDoesNotPassATaskGoalAsAnInventoryPath()
    {
        var profile = Profile("markdown");
        profile = profile with { Target = null, Profile = profile.Profile! with { FileKinds = [new("markdown", 1, ["chapter.md"])] } };
        var result = new ContextWorkflowPlanner().Plan(new("Review the chapter", ProjectContext: "Docs"), profile);
        result.WorkflowType.Should().Be("document_review");
        result.Steps[0].InputHints.Should().NotContainKey("targetPath");
        result.Steps[1].InputHints["query"].Should().Be("Review the chapter");
    }

    [Fact]
    public void MixedRepositoryDocumentTargetDoesNotInheritCallableWorkflows()
    {
        var profile = Profile("markdown");
        var result = new ContextWorkflowPlanner().Plan(new("Edit the chapter", "chapter.md", "Docs"), profile);
        result.Status.Should().Be("valid");
        result.WorkflowType.Should().Be("document_review");
        result.Steps.Select(step => step.Tool).Should().Equal("get_project_profile", "search_documentation");
        result.Applicability!.ProfileGeneration.Should().Be(1);
        result.Applicability.Status.Should().Be("ready");
        var code = new ContextWorkflowPlanner().Plan(new("Edit a class", "src/A.cs", "Docs"), Profile("csharp"));
        code.WorkflowType.Should().Be("before_edit");
        code.Applicability!.Status.Should().Be("unknown");
    }

    [Theory]
    [InlineData("unknown", "enabled", "succeeded", "complete", "full", "unknown")]
    [InlineData("stale", "enabled", "succeeded", "complete", "full", "stale")]
    [InlineData("available", "disabled", "succeeded", "complete", "full", "disabled")]
    [InlineData("available", "enabled", "running", "unknown", "full", "not_indexed")]
    [InlineData("available", "enabled", "partial", "partial", "full", "partial")]
    [InlineData("available", "enabled", "failed", "unknown", "full", "failed")]
    [InlineData("available", "enabled", "succeeded", "unknown", "delta", "unknown")]
    public void UnreadyDocumentEvidenceBlocksTheRoute(string status, string configuration, string state, string coverage, string mode, string expected)
    {
        var profile = Profile("markdown");
        profile = profile with { Status = status, Profile = status == "unknown" ? null : profile.Profile! with {
            Analyzers = [new("documents", true, configuration, "unknown", ["document_search"], [])]
        }, AnalyzerOutcomes = [Outcome() with { State = state, Coverage = coverage, Mode = mode }] };
        var result = new ContextWorkflowPlanner().Plan(new("Review", "chapter.md", "Docs", "document_review"), profile);
        result.Status.Should().Be("blocked");
        result.Applicability!.Status.Should().Be(expected);
    }

    [Fact]
    public void RequiredDocumentStepsCannotBeSilentlyTruncatedAndScriptsRemainUnsupported()
    {
        var planner = new ContextWorkflowPlanner();
        var result = planner.Plan(new("Edit", "chapter.md", "Docs", MaxSteps: 1), Profile("markdown"));
        result.Status.Should().Be("blocked");
        result.Steps.Should().HaveCount(2);
        result.Error.Should().Contain("maxSteps");
        planner.Plan(new("Edit", "Publish.ps1", "Docs"), Profile("powershell"))
            .Applicability!.Status.Should().Be("unsupported");
    }

    [Fact]
    public void MissingDiscoveryKeepsAdviceButBlocksCallableExecution()
    {
        var missing = Profile("csharp") with { Status = "unknown", Profile = null, Target = null };
        var result = new ContextWorkflowPlanner().Plan(new("Edit", "src/A.cs", "Docs"), missing);
        result.Status.Should().Be("blocked");
        result.Applicability!.Status.Should().Be("unknown");
        result.Steps.Should().NotBeEmpty();
        result.Warnings.Should().Contain(w => w.Contains("advisory"));
    }

    [Fact]
    public void NarrowScopeCannotAuthorizeAProjectWideDocumentQuery()
    {
        var profile = Profile("markdown") with { AnalyzerOutcomes = [Outcome() with { Scope = "docs" }],
            Target = new("docs/chapter.md", "observed", "markdown") };
        ContextWorkflowProfilePolicy.EvaluateDocumentSearch(profile).Status.Should().Be("unknown");
    }

    [Theory]
    [InlineData("match", "ready")]
    [InlineData("different", "stale")]
    [InlineData("missing", "stale")]
    public void DocumentReadinessRequiresMatchingPublishedFingerprints(string evidence, string expected)
    {
        var profile = Profile("markdown");
        var hash = "sha256:" + new string('a', 64);
        var fingerprint = new ProjectAnalyzerFingerprint(hash, hash);
        profile = profile with { Profile = profile.Profile! with
        {
            Analyzers = [profile.Profile.Analyzers[0] with { Fingerprint = fingerprint }]
        }, AnalyzerOutcomes = [Outcome() with { Fingerprint = evidence == "missing" ? null
            : evidence == "different" ? fingerprint with { Parser = "sha256:" + new string('b', 64) } : fingerprint }] };

        ContextWorkflowProfilePolicy.EvaluateDocumentSearch(profile).Status.Should().Be(expected);
    }

    [Theory]
    [InlineData("outcome")]
    [InlineData("profile")]
    [InlineData("legacy")]
    [InlineData("mismatch")]
    public void UnboundAndMismatchedSourceContentCannotAuthorizeDocumentSearch(string binding)
    {
        var profile = Profile("markdown");
        profile = profile with
        {
            Profile = profile.Profile! with { SourceFingerprint = binding is "profile" or "legacy" ? null : profile.Profile.SourceFingerprint },
            AnalyzerOutcomes = [Outcome() with { SourceFingerprint = binding is "outcome" or "legacy" ? null
                : binding == "mismatch" ? "sha256:" + new string('b', 64) : Outcome().SourceFingerprint }]
        };
        ContextWorkflowProfilePolicy.EvaluateDocumentSearch(profile).Status.Should().Be("stale");
    }

    [Fact]
    public async Task ExecutionRevalidatesGenerationBeforeDocumentSearch()
    {
        var repository = Substitute.For<IProjectProfileRepository>();
        var stored = Stored();
        repository.GetAsync("Docs", Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(stored, stored, stored with { Generation = 2, LatestGeneration = 2 });
        var vector = Substitute.For<IVectorRepository>();
        var service = Service(repository, vector);
        using var result = JsonDocument.Parse(await service.ExecuteContextWorkflowAsync("Edit the chapter", "chapter.md", "Docs"));
        result.RootElement.GetProperty("status").GetString().Should().Be("blocked");
        result.RootElement.GetProperty("steps")[1].GetProperty("status").GetString().Should().Be("stale");
        await vector.DidNotReceiveWithAnyArgs().SearchByTextAsync(default!, default, default, default);
    }

    [Fact]
    public async Task DocumentSearchExcludesDeletedSourcesAndRechecksAnEmptyQueryGeneration()
    {
        var repository = Substitute.For<IProjectProfileRepository>();
        repository.GetAsync("Docs", Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(Stored());
        repository.GetAsync("Docs", "deleted.md", Arg.Any<CancellationToken>()).Returns(Stored() with { Target = null });
        var vector = Substitute.For<IVectorRepository>();
        vector.SearchByTextAsync("chapter", "Docs", 8, Arg.Any<CancellationToken>()).Returns(
            [new KnowledgeDocument { Id = "old", Content = "stale deleted content", Source = "deleted.md" },
             new KnowledgeDocument { Id = "current", Content = "current chapter text", Source = "chapter.md" }]);
        var service = Service(repository, vector);
        (await service.SearchDocumentationAsync("chapter", "Docs")).Should().Contain("current chapter text").And.NotContain("stale deleted content");
        repository.GetAsync("Docs", Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Stored(), Stored() with { Generation = 2, LatestGeneration = 2 });
        (await service.SearchDocumentationAsync("empty", "Docs")).Should().Contain("status: stale");
    }

    [Fact]
    public async Task DirectDocumentCallsUseTheSameCoverageGateAndPreserveLegitimateEmptyResults()
    {
        var repository = Substitute.For<IProjectProfileRepository>();
        repository.GetAsync("Docs", Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(Stored() with { AnalyzerOutcomes = [] });
        var vector = Substitute.For<IVectorRepository>();
        var service = Service(repository, vector);
        (await service.SearchDocumentationAsync("chapter", "Docs")).Should().Contain("not_indexed");
        await vector.DidNotReceiveWithAnyArgs().SearchByTextAsync(default!, default, default, default);
        repository.GetAsync("Docs", Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(Stored());
        (await service.SearchDocumentationAsync("chapter", "Docs")).Should().Contain("status: ok").And.Contain("No matching");
        await vector.Received(1).SearchByTextAsync("chapter", "Docs", 8, Arg.Any<CancellationToken>());
    }

    private static CodebaseQueryService Service(IProjectProfileRepository repository, IVectorRepository vector) => new(
        Substitute.For<ICodeGraphRepository>(), vector, new NoOpEmbeddingProvider(), Options.Create(new CodebaseAnalysisOptions()),
        Options.Create(new CodebaseIndexingOptions()), new DefaultAnalysisProfilePolicy(),
        new StaticProjectAnalysisOptionsResolver(new CodebaseAnalysisOptions()), new ProjectProfileService(repository));

    private static ProjectAnalyzerOutcome Outcome() => new("documents", ".", "succeeded", "complete", "full", DateTimeOffset.UtcNow, "cli/test", [])
        { SourceFingerprint = "sha256:" + new string('a', 64) };
    private static ProjectProfileResult Profile(string targetKind) => new("available", "Docs", 1, 1, DateTimeOffset.UtcNow,
        new("Docs", DateTimeOffset.UtcNow, "complete", 2, [new("markdown", 1, ["chapter.md"]), new("csharp", 1, ["src/A.cs"])], [], 0,
            [new("documents", true, "enabled", "unknown", ["document_search"], [])], [], 0)
            { SourceFingerprint = "sha256:" + new string('a', 64) },
        new(targetKind == "markdown" ? "chapter.md" : targetKind == "csharp" ? "src/A.cs" : "Publish.ps1", "observed", targetKind), [])
        { AnalyzerOutcomes = [Outcome()] };
    private static StoredProjectProfile Stored() => new(1, 1, DateTimeOffset.UtcNow, Profile("markdown").Profile!, new("chapter.md", "markdown"))
        { AnalyzerOutcomes = [Outcome()] };
}
