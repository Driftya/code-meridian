using System.Text.Json;
using CodeMeridian.Core.RepositoryConstraints;
using FluentAssertions;

namespace CodeMeridian.Core.Tests.RepositoryConstraints;

public sealed class RepositoryConstraintPolicyTests
{
    [Fact]
    public void ContractsRoundTripWithoutParserOrFrameworkTypes()
    {
        var policy = Policy() with
        {
            ReferenceRules = [new("links", "docs", ["docs"], true, ["link", "image"], "Resolve local references.", "warning", [])]
        };
        var copy = JsonSerializer.Deserialize<RepositoryConstraintPolicy>(JsonSerializer.Serialize(policy))!;

        copy.Validate().Should().BeEmpty();
        copy.ReferenceRules.Single().RequireLocalResolution.Should().BeTrue();
        copy.ReferenceRules.Single().AllowedTargetGroups.Should().Equal("docs");
        var result = new RepositoryConstraintResult(3, "incomplete", "partial", "excluded",
            [new("docs-place", "placement", "error", "incomplete", "partial", "Excluded target.", ["repository_inventory"],
                0, 0, false, [])], [new("inventory", "excluded", "Excluded target.")]);
        var restored = JsonSerializer.Deserialize<RepositoryConstraintResult>(JsonSerializer.Serialize(result))!;
        restored.ContractVersion.Should().Be("1.0");
        restored.Generation.Should().Be(3);
        restored.Rules.Single().Status.Should().Be("incomplete");
        restored.Diagnostics.Single().Code.Should().Be("excluded");
    }

    [Theory]
    [InlineData("../docs/**")]
    [InlineData("/docs/**")]
    [InlineData("C:\\docs\\**")]
    [InlineData("docs/[a-z].md")]
    [InlineData("docs/{one,two}.md")]
    [InlineData("docs/**file.md")]
    [InlineData("!docs/**")]
    [InlineData("docs/./**")]
    public void UnsupportedOrUnsafePatternsAreInvalid(string pattern)
    {
        var policy = Policy() with { Groups = [new("docs", [pattern], [])] };
        policy.Validate().Should().Contain(item => item.Code == "invalid_pattern");
    }

    [Fact]
    public void GroupsMayOverlapButRuleIdsMustBeUniqueAcrossFamilies()
    {
        var policy = Policy() with
        {
            Groups = [new("docs", ["**/*.md"], []), new("chapters", ["**/*.md"], ["markdown"])],
            ReferenceRules = [new("place", "chapters", ["absent"], true, ["html"], "Reason", "critical", [])]
        };
        var diagnostics = policy.Validate();
        diagnostics.Should().Contain(item => item.Code == "invalid_id");
        diagnostics.Should().Contain(item => item.Code == "unknown_group");
        diagnostics.Should().Contain(item => item.Code == "unsupported_reference_kind");
        diagnostics.Should().Contain(item => item.Code == "invalid_severity");
        diagnostics.Should().NotContain(item => item.Code == "invalid_group");
    }

    [Fact]
    public void UnknownVersionsNullCollectionsAndEmptyPoliciesAreInvalid()
    {
        (Policy() with { ContractVersion = "2.0" }).Validate().Should().Contain(item => item.Code == "unsupported_version");
        (Policy() with { Groups = null! }).Validate().Should().Contain(item => item.Code == "invalid_collections");
        (Policy() with { PlacementRules = [] }).Validate().Should().Contain(item => item.Code == "empty_policy");
    }

    [Fact]
    public void ExceptionsRequireReasonsAndCannotEscapeTheRepository()
    {
        var policy = Policy();
        policy = policy with { PlacementRules = [policy.PlacementRules[0] with
            { Exceptions = [new("../outside", ""), new("docs/**", "")] }] };
        policy.Validate().Should().Contain(item => item.Code == "invalid_exception");
    }

    internal static RepositoryConstraintPolicy Policy() => new("1.0", [new("docs", ["**/*.md"], [])],
        [new("place", "docs", ["docs/**"], [], "Keep documents in docs.", "error", [])], []);
}
