using System.Text.Json;
using CodeMeridian.Core.Projects;
using CodeMeridian.Core.RepositoryConstraints;
using FluentAssertions;

namespace CodeMeridian.Core.Tests.RepositoryConstraints;

public sealed class RepositoryConstraintEvidenceTests
{
    [Fact]
    public void BoundPolicyCaseAndExclusionsRoundTripWithoutChangingLegacyPayloads()
    {
        var snapshot = Snapshot();
        snapshot.Validate().Should().BeNull();
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var json = JsonSerializer.Serialize(snapshot, options);
        var restored = JsonSerializer.Deserialize<ProjectProfileSnapshot>(json, options)!;
        restored.Validate().Should().BeNull();
        restored.ConstraintEvidence.Should().BeEquivalentTo(snapshot.ConstraintEvidence);
        JsonSerializer.Serialize(snapshot with { ConstraintEvidence = null }, options).Should().NotContain("constraintEvidence");
    }

    [Theory]
    [InlineData("source")]
    [InlineData("configuration")]
    [InlineData("fingerprint")]
    [InlineData("version")]
    [InlineData("case")]
    [InlineData("policy-path")]
    [InlineData("missing-policy")]
    [InlineData("invalid-policy")]
    [InlineData("excluded-file")]
    [InlineData("bad-exclusion")]
    [InlineData("case-collision")]
    public void MalformedOrUnboundEvidenceIsRejectedBeforePublication(string kind)
    {
        var snapshot = Snapshot();
        var evidence = snapshot.ConstraintEvidence!;
        snapshot = kind switch
        {
            "source" => snapshot with { Profile = snapshot.Profile with { SourceFingerprint = null } },
            "configuration" => snapshot with { Profile = snapshot.Profile with { ConfigurationFingerprint = null } },
            "fingerprint" => snapshot with { ConstraintEvidence = evidence with { PolicyFingerprint = "bad" } },
            "version" => snapshot with { ConstraintEvidence = evidence with { ContractVersion = "99" } },
            "case" => snapshot with { ConstraintEvidence = evidence with { CaseSensitive = null } },
            "policy-path" => snapshot with { ConstraintEvidence = evidence with { PolicyPath = "../policy.json" } },
            "missing-policy" => snapshot with { ConstraintEvidence = evidence with { PolicyPath = "missing.json" } },
            "invalid-policy" => snapshot with { ConstraintEvidence = evidence with { Policy = evidence.Policy with { Groups = [] } } },
            "excluded-file" => snapshot with { ConstraintEvidence = evidence with { ExcludedPaths = ["policy.json"] } },
            "bad-exclusion" => snapshot with { ConstraintEvidence = evidence with { ExcludedPaths = ["../outside"] } },
            _ => snapshot with
            {
                Files = [new("policy.json", "configuration"), new("POLICY.json", "configuration")],
                Profile = snapshot.Profile with { FileCount = 2, FileKinds = [new("configuration", 2, ["policy.json"])] }
            }
        };
        snapshot.Validate().Should().NotBeNull();
    }

    private static ProjectProfileSnapshot Snapshot()
    {
        var hash = "sha256:" + new string('a', 64);
        return new(1, new("Docs", DateTimeOffset.UtcNow, "complete", 1,
            [new("configuration", 1, ["policy.json"])], [], 0, [], [], 0)
            { SourceFingerprint = hash, ConfigurationFingerprint = hash }, [new("policy.json", "configuration")])
        {
            ConstraintEvidence = new("policy.json", hash, false,
                new("1.0", [new("documents", ["**/*.md"])],
                    [new("placement", "documents", ["docs/**"], [], "Keep docs under docs.", "error", [])], []), ["excluded"])
        };
    }
}
