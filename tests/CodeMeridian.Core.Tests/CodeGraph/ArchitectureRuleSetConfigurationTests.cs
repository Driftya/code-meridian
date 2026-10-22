using System.Text.Json;
using CodeMeridian.Core.CodeGraph;
using FluentAssertions;

namespace CodeMeridian.Core.Tests.CodeGraph;

public sealed class ArchitectureRuleSetConfigurationTests
{
    [Fact]
    public void LegacyNamespaceConfigurationRetainsLayersDependenciesAndDefaultReasons()
    {
        var rules = ArchitectureRuleSetConfiguration.Parse("""
            { "name": "Onion", "layers": [
                { "id": "Domain", "namespaceContainsAny": [".Domain"] },
                { "id": "Store", "namespaceContainsAny": [".Store"] } ],
              "forbiddenDependencies": [ { "from": "Domain", "to": "Store" } ] }
            """);

        rules.Name.Should().Be("Onion");
        rules.Layers.Select(layer => layer.Id).Should().Equal("Domain", "Store");
        rules.ForbiddenDependencies.Should().ContainSingle(rule => rule.FromLayerId == "Domain"
            && rule.ToLayerId == "Store" && rule.Reason == "Domain must not depend on Store");
        rules.RepositoryConstraints.Should().BeNull();
    }

    [Fact]
    public void DocumentOnlyVersionedPoliciesNeedNoNamespaceLayers()
    {
        var rules = ArchitectureRuleSetConfiguration.Parse(RepositoryPolicy);
        rules.Layers.Should().BeEmpty();
        rules.RepositoryConstraints!.Validate().Should().BeEmpty();
        rules.RepositoryConstraints.ReferenceRules.Single().ReferenceKinds.Should().Equal("image");
    }

    [Fact]
    public void OptionalSelectorsAndRuleCollectionsDefaultToEmpty()
    {
        var rules = ArchitectureRuleSetConfiguration.Parse("""
            { "repositoryConstraints": { "contractVersion": "1.0",
                "groups": [ { "id": "docs", "paths": ["**/*.md"] } ],
                "placementRules": [ { "id": "place", "group": "docs", "allowedPaths": ["docs/**"],
                    "reason": "Keep docs here.", "severity": "error" } ] } }
            """);
        rules.RepositoryConstraints!.Validate().Should().BeEmpty();
        rules.RepositoryConstraints.Groups.Single().FileKinds.Should().BeEmpty();
        rules.RepositoryConstraints.PlacementRules.Single().DeniedPaths.Should().BeEmpty();
        rules.RepositoryConstraints.PlacementRules.Single().Exceptions.Should().BeEmpty();
        rules.RepositoryConstraints.ReferenceRules.Should().BeEmpty();
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"customValidators\":[\"all-clear\"]}")]
    [InlineData("{\"repositoryConstraints\":{\"contractVersion\":\"2.0\"}}")]
    [InlineData("{\"layers\":null}")]
    [InlineData("{\"repositoryConstraints\":null}")]
    [InlineData("{\"layers\":[{\"id\":\"A\",\"namespaceContainsAny\":[\".A\"]}]}")]
    [InlineData("{\"layers\":[{\"id\":\"A\",\"namespaceContainsAny\":[\".A\"]}],\"forbiddenDependencies\":[{\"from\":\"A\",\"to\":\"missing\"}]}")]
    [InlineData("{\"name\":\"one\",\"Name\":\"two\"}")]
    public void InvalidOrCustomPoliciesCannotFallBackToDefaults(string json)
    {
        var action = () => ArchitectureRuleSetConfiguration.Parse(json);
        action.Should().Throw<Exception>().Where(exception => exception.GetType() == typeof(ArgumentException) || exception.GetType() == typeof(JsonException));
    }

    [Fact]
    public void LegacyFlattenedEntriesPreserveConfiguredNamespacePolicy()
    {
        var rules = ArchitectureRuleSetConfiguration.ParseEntries(new Dictionary<string, string>
        {
            ["name"] = "Onion", ["layers:0:id"] = "Domain", ["layers:0:namespaceContainsAny:0"] = ".Domain",
            ["layers:1:id"] = "Store", ["layers:1:namespaceContainsAny:0"] = ".Store",
            ["forbiddenDependencies:0:from"] = "Domain", ["forbiddenDependencies:0:to"] = "Store",
            ["forbiddenDependencies:0:reason"] = "No stores."
        });
        rules.ForbiddenDependencies.Single().Reason.Should().Be("No stores.");
    }

    [Fact]
    public void FlattenedRepositoryPolicyRestoresEmptyCollectionsAndBooleanValues()
    {
        var rules = ArchitectureRuleSetConfiguration.ParseEntries(new Dictionary<string, string>
        {
            ["repositoryConstraints:contractVersion"] = "1.0",
            ["repositoryConstraints:groups:0:id"] = "docs",
            ["repositoryConstraints:groups:0:paths:0"] = "**/*.md",
            ["repositoryConstraints:referenceRules:0:id"] = "links",
            ["repositoryConstraints:referenceRules:0:sourceGroup"] = "docs",
            ["repositoryConstraints:referenceRules:0:requireLocalResolution"] = "True",
            ["repositoryConstraints:referenceRules:0:referenceKinds:0"] = "link",
            ["repositoryConstraints:referenceRules:0:reason"] = "Resolve local links.",
            ["repositoryConstraints:referenceRules:0:severity"] = "error"
        });
        rules.RepositoryConstraints!.Validate().Should().BeEmpty();
        rules.RepositoryConstraints.ReferenceRules.Single().RequireLocalResolution.Should().BeTrue();
    }

    [Theory]
    [InlineData("layers:2:id", "Domain")]
    [InlineData("custom:0:validator", "anything")]
    [InlineData("name", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void MissingUnsupportedOrTruncatedIndexedPoliciesAreRejected(string key, string value)
    {
        var action = () => ArchitectureRuleSetConfiguration.ParseEntries(new Dictionary<string, string> { [key] = value });
        action.Should().Throw<Exception>().Where(exception => exception.GetType() == typeof(ArgumentException) || exception.GetType() == typeof(JsonException));
    }

    private const string RepositoryPolicy = """
        { "name": "Documents", "repositoryConstraints": {
          "contractVersion": "1.0",
          "groups": [ { "id": "docs", "paths": ["**/*.md"], "fileKinds": ["markdown"] },
                      { "id": "assets", "paths": ["docs/images/**"], "fileKinds": [] } ],
          "placementRules": [ { "id": "place", "group": "docs", "allowedPaths": ["docs/**"],
            "deniedPaths": [], "reason": "Keep documents here.", "severity": "error", "exceptions": [] } ],
          "referenceRules": [ { "id": "images", "sourceGroup": "docs", "allowedTargetGroups": ["assets"],
            "requireLocalResolution": true, "referenceKinds": ["image"],
            "reason": "Use local assets.", "severity": "warning", "exceptions": [] } ] } }
        """;
}
