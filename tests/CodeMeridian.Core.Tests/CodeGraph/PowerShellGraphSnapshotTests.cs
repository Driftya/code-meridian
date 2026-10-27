using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Projects;
using FluentAssertions;

namespace CodeMeridian.Core.Tests.CodeGraph;

public sealed class PowerShellGraphSnapshotTests
{
    private const string Hash = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private static readonly ProjectAnalyzerFingerprint Fingerprint = new(Hash, Hash);

    [Fact]
    public void EmptyScopeAndFailedSourcesHaveExplicitValidOutcomes()
    {
        (Snapshot() with { Files = [] }).Validate().Should().BeNull();
        (Snapshot() with { Files = [new("a.ps1", "failed", null, [], [])] }).Validate().Should().BeNull();
        Snapshot().Validate().Should().BeNull();
    }

    [Theory]
    [InlineData("version")]
    [InlineData("generation")]
    [InlineData("fingerprint")]
    [InlineData("source")]
    [InlineData("path")]
    [InlineData("duplicate")]
    [InlineData("status")]
    [InlineData("failedFacts")]
    [InlineData("owner")]
    [InlineData("nodeKind")]
    [InlineData("reservedProperty")]
    [InlineData("missingEndpoint")]
    [InlineData("edgeKind")]
    [InlineData("edgeOwner")]
    [InlineData("duplicateNode")]
    public void MalformedOrForeignFactsAreRejected(string change)
    {
        var snapshot = Snapshot();
        var file = snapshot.Files.Single();
        var node = file.Nodes.Single();
        snapshot = change switch
        {
            "version" => snapshot with { ContractVersion = "2" },
            "generation" => snapshot with { Generation = 0 },
            "fingerprint" => snapshot with { Fingerprint = new("bad", Hash) },
            "source" => snapshot with { SourceFingerprint = "bad" },
            "path" => snapshot with { Files = [file with { Path = "../a.ps1" }] },
            "duplicate" => snapshot with { Files = [file, file] },
            "status" => snapshot with { Files = [file with { Status = "unknown" }] },
            "failedFacts" => snapshot with { Files = [file with { Status = "failed" }] },
            "owner" => snapshot with { Files = [file with { Nodes = [node with { ProjectContext = "Other" }] }] },
            "nodeKind" => snapshot with { Files = [file with { Nodes = [node with { Type = CodeNodeType.Class }] }] },
            "reservedProperty" => snapshot with { Files = [file with { Nodes = [node with { Properties = new() { ["powershellProject"] = "Other" } }] }] },
            "missingEndpoint" => snapshot with { Files = [file with { Edges = [new() { SourceId = node.Id, TargetId = "missing", Type = CodeEdgeType.Contains, SourceFilePath = file.Path }] }] },
            "edgeKind" => snapshot with { Files = [file with { Edges = [new() { SourceId = node.Id, TargetId = node.Id, Type = CodeEdgeType.Uses, SourceFilePath = file.Path }] }] },
            "edgeOwner" => snapshot with { Files = [file with { Edges = [new() { SourceId = node.Id, TargetId = node.Id, Type = CodeEdgeType.Contains, SourceFilePath = "b.ps1" }] }] },
            _ => snapshot with { Files = [file with { Nodes = [node, node] }] }
        };
        snapshot.Validate().Should().NotBeNull();
    }

    [Theory]
    [InlineData("inventory")]
    [InlineData("source")]
    [InlineData("worker")]
    [InlineData("disabled")]
    public void ScopeAndIdentityCannotBeBorrowedFromAnotherProfile(string change)
    {
        var snapshot = Snapshot();
        var profile = new ProjectProfile("Test", DateTimeOffset.UtcNow, "complete", 1, [], [], 0,
            [new("powershell", true, "enabled", "unknown", [], []) { Fingerprint = Fingerprint }], [], 0) { SourceFingerprint = Hash };
        snapshot.ValidateAgainst(profile, [new("a.ps1", "powershell")]).Should().BeNull();
        if (change == "source") profile = profile with { SourceFingerprint = "sha256:" + new string('b', 64) };
        if (change == "worker") snapshot = snapshot with { Fingerprint = new("sha256:" + new string('b', 64), Hash) };
        if (change == "disabled") profile = profile with { Analyzers = [profile.Analyzers[0] with { ConfigurationState = "disabled" }] };
        snapshot.ValidateAgainst(profile, change == "inventory" ? [new("a.ps1", "powershell"), new("b.ps1", "powershell")] : [new("a.ps1", "powershell")]).Should().NotBeNull();
    }

    private static PowerShellGraphSnapshot Snapshot() => new("Test", 1, Hash, Fingerprint,
        [new("a.ps1", "complete", Hash, [new() { Id = "Test::PowerShellFile::a", Name = "a.ps1", Type = CodeNodeType.File,
            FilePath = "a.ps1", SourceHash = Hash, ProjectContext = "Test" }], [])]);
}
