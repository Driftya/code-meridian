using System.Text.Json;
using CodeMeridian.Application.Projects;
using CodeMeridian.Application.RepositoryConstraints;
using CodeMeridian.Application.Services;
using CodeMeridian.Application.Services.ContextWorkflows;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;
using CodeMeridian.Core.Projects;
using CodeMeridian.Core.RepositoryConstraints;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace CodeMeridian.Application.Tests.Services;

public sealed class RepositoryConstraintRoutingTests
{
    [Theory]
    [InlineData("markdown")]
    [InlineData("powershell")]
    [InlineData("sql")]
    [InlineData("asset")]
    public void InventoryConstraintsDoNotRequireAnAnalyzerForTheObservedKind(string kind)
    {
        var profile = Profile(kind);
        var result = ContextWorkflowToolPolicy.Evaluate("find_repository_constraint_violations", profile);
        result.Status.Should().Be("ready");
        result.Coverage.Should().Be("partial");
        ContextWorkflowToolPolicy.Evaluate("get_context_for_editing", profile).Status.Should().NotBe("ready");
    }

    [Fact]
    public void DocumentPlanIncludesReadyConstraintStepOrExplainsItsOmissionWithoutDroppingRequiredSteps()
    {
        var planner = new ContextWorkflowPlanner();
        var profile = Profile("markdown");
        var request = new ContextWorkflowPlanRequest("Edit the chapter", "a.md", "Docs");
        var result = planner.Plan(request, profile);
        result.Steps.Select(step => step.Tool).Should().Equal("get_project_profile", "search_documentation", "find_repository_constraint_violations");
        result.Steps.Last().Required.Should().BeFalse();
        result.Steps.Last().InputHints["targetPath"].Should().Be("a.md");
        var bounded = planner.Plan(request with { MaxSteps = 2 }, profile);
        bounded.Status.Should().Be("valid");
        bounded.Steps.Should().HaveCount(2);
        bounded.OmittedSteps.Should().ContainSingle(step => step.Tool == "find_repository_constraint_violations");
        var legacy = planner.Plan(request, profile with { RepositoryConstraints = null });
        legacy.OmittedSteps.Should().ContainSingle(step => step.Applicability!.Status == "unknown");
        planner.Plan(request with { IncludeOptionalSteps = false }, profile).Steps.Should().HaveCount(2);
    }

    [Theory]
    [InlineData("stale")]
    [InlineData("unknown")]
    [InlineData("failed")]
    public void MissingOrStalePolicyEvidenceCannotAuthorizeConstraintPlans(string state)
    {
        var profile = Profile("markdown");
        profile = profile with { RepositoryConstraints = profile.RepositoryConstraints! with { CanEvaluate = false, Status = state } };
        ContextWorkflowToolPolicy.Evaluate("find_repository_constraint_violations", profile).Status.Should().NotBe("ready");
    }

    [Fact]
    public async Task DocumentOnlyArchitectureReportIncludesIndependentRulesWithoutReadingNamespaceGraph()
    {
        var (service, graph, _) = Service();
        var result = await service.FindArchitectureViolationsAsync("Docs");
        result.Should().Contain("not_applicable").And.Contain("Repository Constraints").And.Contain("location").And.Contain("misplaced");
        await graph.DidNotReceiveWithAnyArgs().FindArchitectureViolationsAsync(default, default);
    }

    [Fact]
    public async Task ArchitectureReportDiscardsBothSectionsWhenAReservationChangesDuringComposition()
    {
        var (service, _, _) = Service(changeDuringReport: true);
        var result = await service.FindArchitectureViolationsAsync("Docs");
        result.Should().Contain("all findings were discarded").And.NotContain("misplaced");
    }

    [Fact]
    public async Task ProfileSummaryIsBoundToTheRequestedFileAndDoesNotAuthorizeSemanticCapabilities()
    {
        var (_, _, profiles) = Service();
        var result = await profiles.GetAsync("Docs", "a.md");
        result.RepositoryConstraints!.CanEvaluate.Should().BeTrue();
        result.RepositoryConstraints.RuleFamilies.Should().Equal("placement");
        result.ToMarkdown().Should().Contain("Repository constraints:");
        ContextWorkflowToolPolicy.Evaluate("resolve_exact_symbol", result).Status.Should().Be("not_applicable");
    }

    [Fact]
    public async Task DocumentWorkflowExecutesPlacementAfterSearchWithoutCallableResolution()
    {
        var (service, graph, _) = Service(searchable: true);
        using var result = JsonDocument.Parse(await service.ExecuteContextWorkflowAsync("Edit the chapter", "a.md", "Docs"));
        result.RootElement.GetProperty("status").GetString().Should().Be("completed");
        var step = result.RootElement.GetProperty("steps")[2];
        step.GetProperty("tool").GetString().Should().Be("find_repository_constraint_violations");
        step.GetProperty("output").GetString().Should().Contain("misplaced");
        await graph.DidNotReceiveWithAnyArgs().GetContextForEditingAsync(default!, default);
    }

    [Fact]
    public async Task ConstraintSummaryFailureDoesNotHideIndependentProfileEvidenceAndCancellationPropagates()
    {
        var repository = Substitute.For<IProjectProfileRepository>();
        var profile = Profile("markdown");
        repository.GetAsync("Docs", Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new StoredProjectProfile(3, 3, DateTimeOffset.UtcNow, profile.Profile!, new("a.md", "markdown")));
        repository.GetRepositoryConstraintsAsync("Docs", Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<StoredRepositoryConstraintSnapshot?>(new IOException("private backend details")));
        var service = new ProjectProfileService(repository, new(repository));
        var result = await service.GetAsync("Docs");
        result.Status.Should().Be("available");
        result.RepositoryConstraints!.Status.Should().Be("failed");
        result.ToMarkdown().Should().NotContain("private backend details");
        repository.GetRepositoryConstraintsAsync("Docs", Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<StoredRepositoryConstraintSnapshot?>(new OperationCanceledException()));
        await FluentActions.Awaiting(() => service.GetAsync("Docs")).Should().ThrowAsync<OperationCanceledException>();
    }

    private static ProjectProfileResult Profile(string kind)
    {
        var hash = "sha256:" + new string('a', 64);
        return new("available", "Docs", 3, 3, DateTimeOffset.UtcNow,
            new("Docs", DateTimeOffset.UtcNow, "complete", 1, [new(kind, 1, ["a.md"])], [], 0,
                [new("documents", true, "enabled", "unknown", ["document_search"], [])], [], 0) { SourceFingerprint = hash },
            new("a.md", "observed", kind), [])
        {
            RepositoryConstraints = new(true, "incomplete", "partial", 3, ["placement", "markdown_reference"], ["Reference fragments remain unverified."]),
            AnalyzerOutcomes = [new("documents", ".", "succeeded", "complete", "full", DateTimeOffset.UtcNow, "test", []) { SourceFingerprint = hash }]
        };
    }

    private static (CodebaseQueryService Service, ICodeGraphRepository Graph, ProjectProfileService Profiles) Service(bool searchable = false, bool changeDuringReport = false)
    {
        var hash = "sha256:" + new string('a', 64);
        var files = new ProjectInventoryFile[] { new("a.md", "markdown"), new("policy.json", "configuration") };
        var profile = new ProjectProfile("Docs", DateTimeOffset.UtcNow, "complete", 2,
            [new("markdown", 1, ["a.md"]), new("configuration", 1, ["policy.json"])], [], 0, [], [], 0)
            { SourceFingerprint = hash, ConfigurationFingerprint = hash };
        if (searchable) profile = profile with { Analyzers = [new("documents", true, "enabled", "unknown", ["document_search"], [])] };
        var policy = new RepositoryConstraintPolicy("1.0", [new("docs", ["**/*.md"])],
            [new("location", "docs", ["docs/**"], [], "Keep documents under docs.", "error", [])], []);
        var repository = Substitute.For<IProjectProfileRepository>();
        var stale = false;
        var reads = 0;
        repository.GetRepositoryConstraintsAsync("Docs", Arg.Any<CancellationToken>()).Returns(_ =>
        {
            if (changeDuringReport && ++reads == 3) stale = true;
            return new StoredRepositoryConstraintSnapshot(3, stale ? 4 : 3, profile, files, new("policy.json", hash, true, policy, []));
        });
        repository.GetAsync("Docs", Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(call =>
            new StoredProjectProfile(3, stale ? 4 : 3, DateTimeOffset.UtcNow, profile, call.ArgAt<string?>(1) is null ? null : files[0])
            {
                AnalyzerOutcomes = searchable ? [new("documents", ".", "succeeded", "complete", "full", DateTimeOffset.UtcNow, "test", [])
                    { SourceFingerprint = hash }] : []
            });
        var profiles = new ProjectProfileService(repository, new(repository));
        var graph = Substitute.For<ICodeGraphRepository>();
        var service = new CodebaseQueryService(graph, Substitute.For<IVectorRepository>(), new NoOpEmbeddingProvider(),
            Options.Create(new CodebaseAnalysisOptions()), Options.Create(new CodebaseIndexingOptions()), new DefaultAnalysisProfilePolicy(),
            new StaticProjectAnalysisOptionsResolver(new CodebaseAnalysisOptions()), profiles);
        return (service, graph, profiles);
    }
}
