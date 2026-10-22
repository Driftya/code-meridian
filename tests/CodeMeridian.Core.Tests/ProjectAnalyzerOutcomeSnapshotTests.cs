using CodeMeridian.Core.Projects;
using FluentAssertions;

namespace CodeMeridian.Core.Tests;

public sealed class ProjectAnalyzerOutcomeSnapshotTests
{
    [Theory]
    [InlineData("succeeded", "complete", "full", true)]
    [InlineData("succeeded", "unknown", "delta", true)]
    [InlineData("running", "unknown", "full", true)]
    [InlineData("partial", "partial", "full", true)]
    [InlineData("failed", "unknown", "full", true)]
    [InlineData("succeeded", "complete", "delta", false)]
    [InlineData("running", "complete", "full", false)]
    [InlineData("failed", "complete", "full", false)]
    [InlineData("ready", "complete", "full", false)]
    public void CompletionAndCoverageAreSeparate(string state, string coverage, string mode, bool valid)
    {
        var error = (Snapshot() with { Outcomes = [Outcome() with { State = state, Coverage = coverage, Mode = mode }] }).Validate();
        if (valid) error.Should().BeNull();
        else error.Should().NotBeNull();
    }

    [Fact]
    public void InvalidVersionsScopesDuplicatesAndNullCollectionsAreRejected()
    {
        (Snapshot() with { ContractVersion = "2" }).Validate().Should().NotBeNull();
        (Snapshot() with { Generation = 0 }).Validate().Should().NotBeNull();
        (Snapshot() with { Outcomes = null! }).Validate().Should().NotBeNull();
        (Snapshot() with { Outcomes = [Outcome() with { Scope = "../outside" }] }).Validate().Should().NotBeNull();
        (Snapshot() with { Outcomes = [Outcome(), Outcome()] }).Validate().Should().NotBeNull();
        (Snapshot() with { Outcomes = [Outcome() with { Fingerprint = new("cli/1.0", "unknown") }] }).Validate().Should().NotBeNull();
        (Snapshot() with { Outcomes = [Outcome() with { SourceFingerprint = "sha256:abc" }] }).Validate().Should().NotBeNull();
    }

    [Fact]
    public void SourceBindingIsOptionalForLegacyUploadsAndRoundTripsWhenKnown()
    {
        Snapshot().Validate().Should().BeNull();
        var snapshot = Snapshot() with { Outcomes = [Outcome() with { SourceFingerprint = "sha256:" + new string('a', 64) }] };
        snapshot.Validate().Should().BeNull();
        var decoded = System.Text.Json.JsonSerializer.Deserialize<ProjectAnalyzerOutcomeSnapshot>(System.Text.Json.JsonSerializer.Serialize(snapshot))!;
        decoded.Outcomes.Single().SourceFingerprint.Should().Be(snapshot.Outcomes.Single().SourceFingerprint);
    }

    private static ProjectAnalyzerOutcome Outcome() => new("documents", ".", "succeeded", "complete", "full", DateTimeOffset.UtcNow, "cli/test", []);
    private static ProjectAnalyzerOutcomeSnapshot Snapshot() => new("Docs", 1, [Outcome()]);
}
