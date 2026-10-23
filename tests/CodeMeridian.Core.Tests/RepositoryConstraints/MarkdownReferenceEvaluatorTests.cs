using CodeMeridian.Core.Projects;
using CodeMeridian.Core.RepositoryConstraints;
using FluentAssertions;

namespace CodeMeridian.Core.Tests.RepositoryConstraints;

public sealed class MarkdownReferenceEvaluatorTests
{
    [Fact]
    public void InventoryResolutionDistinguishesMissingForbiddenExternalUnsupportedAndTraversal()
    {
        var result = Evaluate(Document("../images/ok.png", "../private/no.png", "missing.png", "https://example.invalid/a",
            "data:image/png;base64,foo", "../../outside.png"));
        result.References.Select(item => item.Status).Should().Equal("resolved", "resolved", "missing", "external", "unsupported", "outside_repository");
        var rule = result.Result.Rules.Single();
        rule.Findings.Select(item => item.Status).Should().Equal("forbidden", "missing", "unsupported", "outside_repository");
        rule.Findings.Should().OnlyContain(item => item.Line == 1 && item.Column == 1 && item.Path == "docs/a.md");
        rule.Coverage.Should().Be("partial");
        rule.Findings.First().Reason.Should().Contain("private/no.png").And.Contain("Use approved assets.");
    }

    [Fact]
    public void MissingAndForbiddenFindingsAreCompletedEvaluationRatherThanUnavailableAnalysis()
    {
        var result = Evaluate(Document("missing.png", "../private/no.png"));
        result.Result.Status.Should().Be("evaluated");
        result.Result.Coverage.Should().Be("complete");
        result.Result.Rules.Single().FindingCount.Should().Be(2);
    }

    [Fact]
    public void ExistingLocalFilesAndExternalLinksCanHaveZeroFindings()
    {
        var result = Evaluate(Document("../images/ok.png", "https://example.invalid/a"));
        result.Result.Status.Should().Be("evaluated");
        result.Result.Rules.Single().Findings.Should().BeEmpty();
        Evaluate(Document()).Result.Status.Should().Be("evaluated");
    }

    [Fact]
    public void EveryOverlappingSourceGroupIsEvaluatedAndAnyAllowedTargetGroupCanMatch()
    {
        var policy = Policy();
        policy = policy with { Groups = [.. policy.Groups, new("same-docs", ["docs/**"], [])], ReferenceRules = [
            policy.ReferenceRules[0] with { AllowedTargetGroups = ["docs", "images"] },
            policy.ReferenceRules[0] with { Id = "second", SourceGroup = "same-docs", AllowedTargetGroups = ["docs"] }] };
        var result = Evaluate(Document("../images/ok.png"), policy: policy);
        result.Result.Rules.Select(item => item.RuleId).Should().Equal("assets", "second");
        result.Result.Rules[0].Findings.Should().BeEmpty();
        result.Result.Rules[1].Findings.Should().ContainSingle(item => item.Status == "forbidden");
    }

    [Fact]
    public void SourceAndTargetGroupsRespectObservedFileKinds()
    {
        var policy = Policy() with { Groups = [new("docs", ["docs/**"], ["sql"]), new("images", ["images/**"], ["asset"])] };
        Evaluate(Document("../images/ok.png"), policy: policy).Result.Rules.Single().Status.Should().Be("not_applicable");
        policy = policy with { Groups = [new("docs", ["docs/**"], ["markdown"]), new("images", ["images/**"], ["sql"])] };
        Evaluate(Document("../images/ok.png"), policy: policy).Result.Rules.Single().Findings.Should().ContainSingle(item => item.Status == "forbidden");
    }

    [Fact]
    public void ExcludedAndUnobservedTargetsNeverMasqueradeAsMissingOrCleanValidation()
    {
        var inventory = Inventory() with { DiscoveryState = "partial", ExcludedPaths = ["generated"] };
        var result = Evaluate(Document("../generated/a.png", "unknown.png"), inventory);
        result.References.Select(item => item.Status).Should().Equal("excluded", "unobserved");
        result.Result.Status.Should().Be("incomplete");
        result.Result.Rules.Single().Coverage.Should().Be("partial");
        Evaluate(Document("../generated/a.png"), inventory with { DiscoveryState = "complete" })
            .References.Single().Status.Should().Be("excluded");
    }

    [Theory]
    [InlineData(true, "missing")]
    [InlineData(false, "resolved")]
    public void CaseSensitivityDoesNotDependOnExecutionHost(bool sensitive, string expected)
    {
        Evaluate(Document("../Images/ok.png"), Inventory() with { CaseSensitive = sensitive })
            .References.Single().Status.Should().Be(expected);
    }

    [Fact]
    public void FragmentsAreExplicitlyUnverifiedAndDoNotClaimAnchorValidation()
    {
        var result = Evaluate(Document("#part", "../images/ok.png#part"));
        result.References.Select(item => item.Status).Should().Equal("fragment_only", "resolved");
        result.References.Should().OnlyContain(item => item.FragmentStatus == "unverified");
        result.Result.Status.Should().Be("incomplete");
        result.Result.Rules.Single().Findings.Should().Contain(item => item.Status == "unverified_fragment");
    }

    [Fact]
    public void ExceptionsAndReferenceKindFiltersAreExplicit()
    {
        var policy = Policy();
        var excepted = policy with { ReferenceRules = [policy.ReferenceRules[0] with { Exceptions = [new("docs/a.md", "Legacy entry point.")] }] };
        var result = Evaluate(Document("missing.png"), policy: excepted);
        result.Result.Rules.Single().Findings.Should().ContainSingle(item => item.Status == "excepted" && item.Reason == "Legacy entry point.");
        var links = policy with { ReferenceRules = [policy.ReferenceRules[0] with { ReferenceKinds = ["link"] }] };
        Evaluate(Document("missing.png"), policy: links).Result.Rules.Single().Findings.Should().BeEmpty();
    }

    [Theory]
    [InlineData("complete", "missing")]
    [InlineData("partial", "unobserved")]
    public void UnobservedSourcesDoNotContributeFacts(string discovery, string status)
    {
        var result = Evaluate(Document("missing.png"), Inventory() with { DiscoveryState = discovery, Files = [] });
        result.Result.TargetStatus.Should().Be(status);
        result.Result.Status.Should().Be("incomplete");
        result.References.Should().BeEmpty();
    }

    [Fact]
    public void StaleCrossProjectMalformedAndFailedParserEvidenceCannotAuthorizeEvaluation()
    {
        Evaluate(Document() with { Generation = 6 }).Result.Status.Should().Be("incomplete");
        Evaluate(Document() with { Project = "Other" }).Result.Status.Should().Be("incomplete");
        Evaluate(Document() with { ParserFingerprint = "unknown" }).Result.Status.Should().Be("incomplete");
        Evaluate(Document() with { Status = "incomplete", Diagnostics = [new("docs/a.md", "unsupported_html", "HTML.")] })
            .Result.Status.Should().Be("incomplete");
        var fact = Document("x.png").References.Single() with { NormalizedPath = "forged.png" };
        Evaluate(Document() with { References = [fact] }).Result.Status.Should().Be("incomplete");
        Evaluate(Document(), Inventory() with { Files = [new("docs/a.md", "markdown"), new("docs/A.md", "markdown")], CaseSensitive = false })
            .Result.Status.Should().Be("incomplete");
        Evaluate(Document(), Inventory() with { ExcludedPaths = ["docs"] }).Result.Status.Should().Be("incomplete");
    }

    [Fact]
    public void MissingTargetCanBeAllowedWithoutClaimingTargetGroupMembership()
    {
        var policy = Policy();
        policy = policy with { ReferenceRules = [policy.ReferenceRules[0] with { RequireLocalResolution = false }] };
        var result = Evaluate(Document("missing.png"), policy: policy);
        result.References.Single().Status.Should().Be("missing");
        result.Result.Status.Should().Be("incomplete");
        result.Result.Rules.Single().Findings.Should().ContainSingle(item => item.Status == "unverified_target_group");
    }

    [Fact]
    public void FindingsAreBoundedWithAccurateTotalsAndUnmatchedRulesAreInapplicable()
    {
        var result = MarkdownReferenceEvaluator.Evaluate(Policy(), Inventory(), Document("a.png", "b.png"), findingLimit: 1);
        result.Result.Rules.Single().FindingCount.Should().Be(2);
        result.Result.Rules.Single().FindingsTruncated.Should().BeTrue();
        var policy = Policy() with { Groups = [new("docs", ["other/**"], []), new("images", ["images/**"], [])] };
        Evaluate(Document(), policy: policy).Result.Rules.Single().Status.Should().Be("not_applicable");
    }

    [Fact]
    public void InvalidPolicyAndCancellationPropagateWithoutCleanResults()
    {
        Evaluate(Document(), policy: Policy() with { ContractVersion = "2" }).Result.Status.Should().Be("invalid");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var action = () => MarkdownReferenceEvaluator.Evaluate(Policy(), Inventory(), Document(), cancellationToken: cancellation.Token);
        action.Should().Throw<OperationCanceledException>();
    }

    private static MarkdownReferenceEvaluation Evaluate(MarkdownReferenceDocument document, RepositoryConstraintInventory? inventory = null,
        RepositoryConstraintPolicy? policy = null) => MarkdownReferenceEvaluator.Evaluate(policy ?? Policy(), inventory ?? Inventory(), document);

    private static RepositoryConstraintPolicy Policy() => new("1.0", [new("docs", ["docs/**"], []), new("images", ["images/**"], [])], [],
        [new("assets", "docs", ["images"], true, ["image"], "Use approved assets.", "error", [])]);

    private static RepositoryConstraintInventory Inventory() => new(7, "complete", true,
        [new("docs/a.md", "markdown"), new("images/ok.png", "asset"), new("private/no.png", "asset")], []) { Project = "Docs" };

    private static MarkdownReferenceDocument Document(params string[] targets) => new("Docs", "Docs::doc::docs/a.md", "docs/a.md", 7,
        "sha256:" + new string('B', 64), "sha256:" + new string('C', 64), "complete", targets.Select(target =>
        {
            var normalized = MarkdownReferenceTarget.Normalize(target, "docs/a.md");
            return new MarkdownReferenceFact("image", new(0, 10, 1, 1), new(4, target.Length, 1, 5), target, target,
                normalized.Kind, normalized.Path, normalized.Fragment);
        }).ToArray(), []);
}
