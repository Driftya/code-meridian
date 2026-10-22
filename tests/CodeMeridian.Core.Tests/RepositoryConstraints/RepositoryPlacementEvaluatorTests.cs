using CodeMeridian.Core.Projects;
using CodeMeridian.Core.RepositoryConstraints;
using FluentAssertions;

namespace CodeMeridian.Core.Tests.RepositoryConstraints;

public sealed class RepositoryPlacementEvaluatorTests
{
    [Theory]
    [InlineData("chapter.md", "markdown")]
    [InlineData("schema.sql", "sql")]
    [InlineData("script.ps1", "powershell")]
    [InlineData("image.png", "asset")]
    public void PlacementUsesInventoryWithoutSemanticAnalyzers(string path, string kind)
    {
        var policy = RepositoryConstraintPolicyTests.Policy() with { Groups = [new("docs", ["**"], [kind])] };
        var result = RepositoryPlacementEvaluator.Evaluate(policy, Inventory(new ProjectInventoryFile(path, kind)));

        result.Status.Should().Be("evaluated");
        result.Coverage.Should().Be("complete");
        result.Rules.Single().Findings.Should().ContainSingle(item => item.Path == path && item.Status == "misplaced");
        result.Rules.Single().RequiredCapabilities.Should().Equal("repository_inventory");
    }

    [Fact]
    public void EveryOverlappingGroupIsEvaluatedAndDenyTakesPrecedenceOverAllow()
    {
        var policy = RepositoryConstraintPolicyTests.Policy() with
        {
            Groups = [new("docs", ["**/*.md"], []), new("drafts", ["docs/**"], [])],
            PlacementRules = [new("first", "docs", ["docs/**"], ["**/draft.md"], "No drafts.", "error", []),
                new("second", "drafts", ["published/**"], [], "Publish here.", "warning", [])]
        };
        var result = RepositoryPlacementEvaluator.Evaluate(policy, Inventory(new ProjectInventoryFile("docs/draft.md", "markdown")));

        result.Rules.Select(rule => rule.RuleId).Should().Equal("first", "second");
        result.Rules[0].Findings.Single().Status.Should().Be("forbidden");
        result.Rules[1].Findings.Single().Status.Should().Be("misplaced");
    }

    [Fact]
    public void ExplicitExceptionRecordsItsReasonEvenForDeniedPaths()
    {
        var policy = RepositoryConstraintPolicyTests.Policy();
        policy = policy with { PlacementRules = [policy.PlacementRules[0] with
            { DeniedPaths = ["**"], Exceptions = [new("README.md", "Repository entry point.")] }] };

        var outcome = RepositoryPlacementEvaluator.Evaluate(policy, Inventory(new ProjectInventoryFile("README.md", "markdown"))).Rules.Single();

        outcome.Findings.Should().ContainSingle(item => item.Status == "excepted" && item.Reason == "Repository entry point.");
    }

    [Theory]
    [InlineData(true, "misplaced")]
    [InlineData(false, null)]
    public void PathCaseIsExplicitAndSeparatorsAndDotSegmentsAreNormalized(bool caseSensitive, string? expected)
    {
        var inventory = Inventory(new ProjectInventoryFile("Docs\\a b.md", "markdown")) with { CaseSensitive = caseSensitive };
        var result = RepositoryPlacementEvaluator.Evaluate(RepositoryConstraintPolicyTests.Policy(), inventory, "Docs/./child/../a b.md");

        result.TargetStatus.Should().Be("observed");
        result.Rules.Single().EvaluatedFiles.Should().Be(1);
        result.Rules.Single().Findings.Select(item => item.Status).Should().Equal(expected is null ? [] : [expected]);
    }

    [Theory]
    [InlineData("complete", "absent.md", "missing")]
    [InlineData("partial", "absent.md", "unobserved")]
    [InlineData("complete", "generated/file.md", "excluded")]
    [InlineData("partial", "generated/file.md", "excluded")]
    public void AbsentTargetsNeverProduceCleanValidation(string discovery, string target, string expected)
    {
        var inventory = Inventory() with { DiscoveryState = discovery, ExcludedPaths = ["generated"] };
        var result = RepositoryPlacementEvaluator.Evaluate(RepositoryConstraintPolicyTests.Policy(), inventory, target);

        result.Status.Should().Be("incomplete");
        result.TargetStatus.Should().Be(expected);
        result.Rules.Single().Status.Should().Be("incomplete");
        result.Rules.Single().EvaluatedFiles.Should().Be(0);
    }

    [Fact]
    public void PartialDiscoveryRetainsFindingsAndNeverInfersInapplicability()
    {
        var inventory = Inventory(new ProjectInventoryFile("wrong.md", "markdown")) with { DiscoveryState = "partial" };
        var result = RepositoryPlacementEvaluator.Evaluate(RepositoryConstraintPolicyTests.Policy(), inventory);
        result.Rules.Single().Coverage.Should().Be("partial");
        result.Rules.Single().Findings.Should().HaveCount(1);
        var empty = RepositoryPlacementEvaluator.Evaluate(RepositoryConstraintPolicyTests.Policy(), inventory with { Files = [] });
        empty.Rules.Single().Status.Should().Be("incomplete");
    }

    [Fact]
    public void CompleteEmptyGroupIsInapplicableAndZeroViolationsAreEvaluated()
    {
        var empty = RepositoryPlacementEvaluator.Evaluate(RepositoryConstraintPolicyTests.Policy(), Inventory(new ProjectInventoryFile("a.sql", "sql")));
        empty.Rules.Single().Status.Should().Be("not_applicable");
        var clean = RepositoryPlacementEvaluator.Evaluate(RepositoryConstraintPolicyTests.Policy(), Inventory(new ProjectInventoryFile("docs/a.md", "markdown")));
        clean.Rules.Single().Status.Should().Be("evaluated");
        clean.Rules.Single().Findings.Should().BeEmpty();
    }

    [Theory]
    [InlineData("../outside.md")]
    [InlineData("/outside.md")]
    [InlineData("C:\\outside.md")]
    public void UnsafeTargetsAreNotTraversed(string target)
    {
        var result = RepositoryPlacementEvaluator.Evaluate(RepositoryConstraintPolicyTests.Policy(), Inventory(), target);
        result.Status.Should().Be("incomplete");
        result.Diagnostics.Should().Contain(item => item.Code == "invalid_inventory_or_target");
    }

    [Fact]
    public void CaseCollisionsAndContradictoryExclusionsAreIncomplete()
    {
        var inventory = Inventory(new ProjectInventoryFile("a.md", "markdown"), new("A.md", "markdown")) with { CaseSensitive = false };
        RepositoryPlacementEvaluator.Evaluate(RepositoryConstraintPolicyTests.Policy(), inventory).Status.Should().Be("incomplete");
        inventory = Inventory(new ProjectInventoryFile("docs/a.md", "markdown")) with { ExcludedPaths = ["docs"] };
        RepositoryPlacementEvaluator.Evaluate(RepositoryConstraintPolicyTests.Policy(), inventory).Status.Should().Be("incomplete");
    }

    [Fact]
    public void BoundedFindingsRetainTheTotalAndStablePathOrdering()
    {
        var result = RepositoryPlacementEvaluator.Evaluate(RepositoryConstraintPolicyTests.Policy(),
            Inventory(new ProjectInventoryFile("z.md", "markdown"), new("a.md", "markdown")), findingLimit: 1);
        var rule = result.Rules.Single();
        rule.FindingCount.Should().Be(2);
        rule.FindingsTruncated.Should().BeTrue();
        rule.Findings.Single().Path.Should().Be("a.md");
    }

    [Fact]
    public void InvalidPoliciesAndUnsupportedReferencesCannotProduceCleanResults()
    {
        var policy = RepositoryConstraintPolicyTests.Policy();
        RepositoryPlacementEvaluator.Evaluate(policy with { ContractVersion = "2" }, Inventory()).Status.Should().Be("invalid");
        policy = policy with { ReferenceRules = [new("links", "docs", [], true, ["link"], "Resolve links.", "error", [])] };
        var result = RepositoryPlacementEvaluator.Evaluate(policy, Inventory(new ProjectInventoryFile("docs/a.md", "markdown")));
        result.Status.Should().Be("unsupported");
        result.Rules.Single(rule => rule.Family == "markdown_reference").Status.Should().Be("unsupported");
    }

    [Fact]
    public void CancellationPropagates()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var action = () => RepositoryPlacementEvaluator.Evaluate(RepositoryConstraintPolicyTests.Policy(), Inventory(), cancellationToken: cancellation.Token);
        action.Should().Throw<OperationCanceledException>();
    }

    private static RepositoryConstraintInventory Inventory(params ProjectInventoryFile[] files) => new(1, "complete", true, files, []);
}
