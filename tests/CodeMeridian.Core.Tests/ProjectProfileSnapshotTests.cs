using CodeMeridian.Core.Projects;
using FluentAssertions;

namespace CodeMeridian.Core.Tests;

public sealed class ProjectProfileSnapshotTests
{
    [Fact]
    public void CompleteInventoryValidatesWithoutClaimingIndexingReadiness()
    {
        Snapshot().Validate().Should().BeNull();
        var snapshot = Snapshot();
        (snapshot with { Profile = snapshot.Profile with {
            Analyzers = [new("documents", true, "enabled", "complete", ["document_search"], [])]
        } }).Validate().Should().Contain("readiness");
    }

    [Theory]
    [InlineData("../README.md")]
    [InlineData("/README.md")]
    [InlineData("C:/README.md")]
    [InlineData("docs//README.md")]
    [InlineData("docs/./README.md")]
    [InlineData("")]
    public void InvalidPathsAreRejected(string path)
    {
        var act = () => ProjectProfilePath.Normalize(path);
        act.Should().Throw<ArgumentException>();
        (Snapshot() with { Files = [new(path, "markdown")] }).Validate().Should().NotBeNull();
    }

    [Fact]
    public void PartialDiscoveryAndInconsistentCountsCannotReplaceInventory()
    {
        var snapshot = Snapshot();
        (snapshot with { Profile = snapshot.Profile with { DiscoveryState = "partial" } }).Validate().Should().NotBeNull();
        (snapshot with { Profile = snapshot.Profile with { FileCount = 2 } }).Validate().Should().NotBeNull();
        (snapshot with { Files = [new("different.md", "markdown")] }).Validate().Should().NotBeNull();
        (snapshot with { Generation = 0 }).Validate().Should().NotBeNull();
        (snapshot with { ContractVersion = "2" }).Validate().Should().NotBeNull();
        (snapshot with { Files = null! }).Validate().Should().NotBeNull();
    }

    [Theory]
    [InlineData("bad")]
    [InlineData("sha256:abc")]
    [InlineData("")]
    public void InvalidFingerprintMetadataIsRejected(string value)
    {
        var snapshot = Snapshot();
        (snapshot with { Profile = snapshot.Profile with { ConfigurationFingerprint = value } }).Validate().Should().NotBeNull();
        (snapshot with { Profile = snapshot.Profile with { SourceFingerprint = value } }).Validate().Should().NotBeNull();
        (snapshot with { Profile = snapshot.Profile with { Analyzers = [snapshot.Profile.Analyzers[0] with
            { Fingerprint = new(value, value) }] } }).Validate().Should().NotBeNull();
    }

    [Fact]
    public void OptionalRevisionAndFingerprintsRoundTripWhileLegacyProfilesRemainValid()
    {
        var snapshot = Snapshot();
        snapshot.Validate().Should().BeNull();
        var fingerprint = "sha256:" + new string('a', 64);
        var enriched = snapshot with { Profile = snapshot.Profile with
        {
            Revision = new(new string('a', 40), true), ConfigurationFingerprint = fingerprint, SourceFingerprint = fingerprint,
            Analyzers = [snapshot.Profile.Analyzers[0] with { Fingerprint = new(fingerprint, fingerprint) }]
        } };
        enriched.Validate().Should().BeNull();
        var decoded = System.Text.Json.JsonSerializer.Deserialize<ProjectProfileSnapshot>(System.Text.Json.JsonSerializer.Serialize(enriched))!;
        decoded.Profile.Revision.Should().Be(enriched.Profile.Revision);
        decoded.Profile.SourceFingerprint.Should().Be(enriched.Profile.SourceFingerprint);
        decoded.Profile.Analyzers[0].Fingerprint.Should().Be(enriched.Profile.Analyzers[0].Fingerprint);
        (snapshot with { Profile = snapshot.Profile with { Revision = new("not-a-commit", false) } }).Validate().Should().NotBeNull();
    }

    private static ProjectProfileSnapshot Snapshot() => new(1,
        new("Docs", DateTimeOffset.UtcNow, "complete", 1, [new("markdown", 1, ["README.md"])],
            [new("markdown", ".", null)], 1, [new("documents", true, "enabled", "unknown", ["document_search"], [])], [], 0),
        [new("README.md", "markdown")]);
}
