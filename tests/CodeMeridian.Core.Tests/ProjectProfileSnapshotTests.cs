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

    private static ProjectProfileSnapshot Snapshot() => new(1,
        new("Docs", DateTimeOffset.UtcNow, "complete", 1, [new("markdown", 1, ["README.md"])],
            [new("markdown", ".", null)], 1, [new("documents", true, "enabled", "unknown", ["document_search"], [])], [], 0),
        [new("README.md", "markdown")]);
}
