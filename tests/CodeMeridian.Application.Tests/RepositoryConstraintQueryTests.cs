using CodeMeridian.Application.RepositoryConstraints;
using CodeMeridian.Core.Projects;
using CodeMeridian.Core.RepositoryConstraints;
using FluentAssertions;
using NSubstitute;

namespace CodeMeridian.Application.Tests;

public sealed class RepositoryConstraintQueryTests
{
    [Fact]
    public async Task DocumentOnlyInventoryEvaluatesEveryRuleWithoutCallablePrerequisitesAndBoundsTheWholeResponse()
    {
        var (service, _) = Create();
        var result = await service.FindAsync("Docs", findingLimit: 1);
        result.Status.Should().Be("evaluated");
        result.Coverage.Should().Be("complete");
        result.Evaluation!.Rules.Should().HaveCount(2);
        result.Evaluation.Rules.Should().OnlyContain(rule => rule.Status == "evaluated" && rule.FindingCount == 2);
        result.Evaluation.Rules.SelectMany(rule => rule.Findings).Should().ContainSingle()
            .Which.Should().Be(new RepositoryConstraintFinding("a.md", "misplaced", "Keep documents under docs."));
        result.Evaluation.Rules.Should().OnlyContain(rule => rule.FindingsTruncated);
        result.ToMarkdown().Should().Contain("docs-location").And.Contain("misplaced").And.Contain("coverage=complete").And.Contain("truncated=true");
    }

    [Theory]
    [InlineData("a.md", "observed", "evaluated")]
    [InlineData("docs/./a.md", "missing", "incomplete")]
    [InlineData("excluded/a.md", "excluded", "incomplete")]
    [InlineData("A.md", "missing", "incomplete")]
    public async Task ExactTargetUsesPublishedCaseAndExclusions(string target, string targetStatus, string status)
    {
        var (service, _) = Create();
        var result = await service.FindAsync("Docs", target);
        result.Status.Should().Be(status);
        result.Evaluation!.TargetStatus.Should().Be(targetStatus);
    }

    [Fact]
    public async Task ExplicitCaseInsensitiveInventoryPreservesObservedPath()
    {
        var stored = Snapshot();
        stored = stored with { Evidence = stored.Evidence! with { CaseSensitive = false } };
        var (service, _) = Create(stored);
        var result = await service.FindAsync("Docs", "A.MD");
        result.Evaluation!.TargetStatus.Should().Be("observed");
        result.Evaluation.Rules[0].Findings[0].Path.Should().Be("a.md");
    }

    [Fact]
    public async Task CompleteObservedTargetOutsideTheSourceGroupsIsExplicitlyNotApplicable()
    {
        var result = await Create().Service.FindAsync("Docs", "policy.json");
        result.Coverage.Should().Be("complete");
        result.Evaluation!.Rules.Should().OnlyContain(rule => rule.Status == "not_applicable"
            && rule.EvaluatedFiles == 0 && rule.FindingCount == 0);
    }

    [Fact]
    public async Task ApplicableReferenceRulesRemainUnsupported()
    {
        var stored = Snapshot();
        stored = stored with { Evidence = stored.Evidence! with { Policy = stored.Evidence.Policy with
        {
            ReferenceRules = [new("references", "documents", [], true, ["link"], "Resolve local links.", "error", [])]
        } } };
        var result = await Create(stored).Service.FindAsync("Docs");
        result.Status.Should().Be("unsupported");
        result.Coverage.Should().Be("unknown");
        result.Evaluation!.Rules.Last().Status.Should().Be("unsupported");
    }

    [Theory]
    [InlineData("missing", "incomplete", "missing_inventory")]
    [InlineData("legacy", "incomplete", "missing_constraint_evidence")]
    [InlineData("stale", "stale", "stale_inventory")]
    [InlineData("invalid", "invalid", "invalid_constraint_evidence")]
    [InlineData("other-project", "incomplete", "invalid_inventory")]
    public async Task UnavailableEvidenceNeverProducesCleanValidation(string kind, string status, string code)
    {
        var stored = Snapshot();
        var (service, repository) = Create(kind switch
        {
            "legacy" => stored with { Evidence = null },
            "stale" => stored with { LatestGeneration = 8 },
            "invalid" => stored with { Profile = stored.Profile with { SourceFingerprint = null } },
            "other-project" => stored with { Profile = stored.Profile with { Project = "Other" } },
            _ => stored
        });
        if (kind == "missing") repository.GetRepositoryConstraintsAsync("Docs", Arg.Any<CancellationToken>()).Returns((StoredRepositoryConstraintSnapshot?)null);
        var result = await service.FindAsync("Docs");
        result.Status.Should().Be(status);
        result.Coverage.Should().Be("unknown");
        result.Evaluation.Should().BeNull();
        result.Diagnostics.Should().ContainSingle().Which.Code.Should().Be(code);
        await repository.DidNotReceive().GetAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("generation")]
    [InlineData("reservation")]
    [InlineData("source")]
    [InlineData("configuration")]
    [InlineData("project")]
    [InlineData("missing")]
    public async Task RevalidationDiscardsAllFindingsWhenEvidenceChanges(string change)
    {
        var stored = Snapshot();
        var (service, repository) = Create(stored);
        var current = new StoredProjectProfile(7, 7, DateTimeOffset.UtcNow, stored.Profile, null);
        current = change switch
        {
            "generation" => current with { Generation = 8, LatestGeneration = 8 },
            "reservation" => current with { LatestGeneration = 8 },
            "source" => current with { Profile = current.Profile with { SourceFingerprint = Hash('b') } },
            "configuration" => current with { Profile = current.Profile with { ConfigurationFingerprint = Hash('b') } },
            "project" => current with { Profile = current.Profile with { Project = "Other" } },
            _ => current
        };
        repository.GetAsync("Docs", cancellationToken: Arg.Any<CancellationToken>()).Returns(change == "missing" ? null : current);
        var result = await service.FindAsync("Docs");
        result.Status.Should().Be("stale");
        result.Evaluation.Should().BeNull();
        result.Diagnostics[0].Code.Should().Be("generation_changed");
    }

    [Theory]
    [InlineData("", null, 100)]
    [InlineData(" Docs", null, 100)]
    [InlineData("Docs", "../a.md", 100)]
    [InlineData("Docs", "/a.md", 100)]
    [InlineData("Docs", null, 0)]
    [InlineData("Docs", null, 1001)]
    public async Task InvalidRequestsFailBeforeReadingStorage(string project, string? target, int limit)
    {
        var (service, repository) = Create();
        await FluentActions.Awaiting(() => service.FindAsync(project, target, limit)).Should().ThrowAsync<ArgumentException>();
        await repository.DidNotReceive().GetRepositoryConstraintsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CancellationIsForwardedAndObservedAfterRevalidation()
    {
        using var cancellation = new CancellationTokenSource();
        var stored = Snapshot();
        var (service, repository) = Create(stored);
        repository.GetAsync("Docs", cancellationToken: cancellation.Token).Returns(_ =>
        {
            cancellation.Cancel();
            return new StoredProjectProfile(7, 7, DateTimeOffset.UtcNow, stored.Profile, null);
        });
        await FluentActions.Awaiting(() => service.FindAsync("Docs", cancellationToken: cancellation.Token)).Should().ThrowAsync<OperationCanceledException>();
        await repository.Received(1).GetRepositoryConstraintsAsync("Docs", cancellation.Token);
    }

    private static (RepositoryConstraintQueryService Service, IProjectProfileRepository Repository) Create(StoredRepositoryConstraintSnapshot? stored = null)
    {
        stored ??= Snapshot();
        var repository = Substitute.For<IProjectProfileRepository>();
        repository.GetRepositoryConstraintsAsync("Docs", Arg.Any<CancellationToken>()).Returns(stored);
        repository.GetAsync("Docs", cancellationToken: Arg.Any<CancellationToken>())
            .Returns(new StoredProjectProfile(stored.Generation, stored.LatestGeneration, DateTimeOffset.UtcNow, stored.Profile, null));
        return (new(repository), repository);
    }

    private static string Hash(char value) => "sha256:" + new string(value, 64);

    private static StoredRepositoryConstraintSnapshot Snapshot() => new(7, 7,
        new("Docs", DateTimeOffset.UtcNow, "complete", 3,
            [new("markdown", 2, ["a.md", "b.md"]), new("configuration", 1, ["policy.json"])], [], 0, [], [], 0)
            { SourceFingerprint = Hash('a'), ConfigurationFingerprint = Hash('a') },
        [new("a.md", "markdown"), new("b.md", "markdown"), new("policy.json", "configuration")],
        new("policy.json", Hash('a'), true, new("1.0", [new("documents", ["**/*.md"])],
            [new("docs-location", "documents", ["docs/**"], [], "Keep documents under docs.", "error", []),
             new("docs-location-also", "documents", ["docs/**"], [], "Keep documents under docs.", "error", [])], []), ["excluded"]));
}
