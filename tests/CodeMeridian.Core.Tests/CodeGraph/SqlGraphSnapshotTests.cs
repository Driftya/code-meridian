using CodeMeridian.Core.CodeGraph;
using FluentAssertions;

namespace CodeMeridian.Core.Tests.CodeGraph;

public sealed class SqlGraphSnapshotTests
{
    [Fact]
    public void Validate_RejectsForeignNodesAndMissingEndpoints()
    {
        var snapshot = Snapshot(new CodeNode { Id = "Other::Sql::File", Name = "a.sql", Type = CodeNodeType.File, ProjectContext = "Other", FilePath = "a.sql" });
        snapshot.Validate().Should().NotBeNull();
        snapshot = Snapshot(FileNode());
        snapshot.Validate().Should().BeNull();
        var file = snapshot.Files[0] with { Edges = [new CodeEdge { SourceId = FileNode().Id, TargetId = "SQL::Sql::missing", Type = CodeEdgeType.Reads, SourceFilePath = "a.sql" }] };
        (snapshot with { Files = [file] }).Validate().Should().NotBeNull();
    }

    [Fact]
    public void Validate_RejectsUnsafePathsOwnershipCollisionsAndMalformedCollections()
    {
        var snapshot = Snapshot(FileNode());
        (snapshot with { Files = [snapshot.Files[0] with { Path = "../a.sql" }] }).Validate().Should().NotBeNull();
        (snapshot with { Files = [null!] }).Validate().Should().NotBeNull();
        (snapshot with { Files = [snapshot.Files[0] with { Nodes = [null!] }] }).Validate().Should().NotBeNull();
        var other = snapshot.Files[0] with { Path = "b.sql", Nodes = [FileNode() with { FilePath = "b.sql" }] };
        (snapshot with { Files = [snapshot.Files[0], other] }).Validate().Should().NotBeNull();
    }

    [Fact]
    public void Validate_RejectsOwnershipMetadataInjection()
    {
        var node = FileNode() with { Properties = new() { ["sqlProject"] = "Other" } };
        Snapshot(node).Validate().Should().NotBeNull();
    }

    private static CodeNode FileNode() => new() { Id = "SQL::Sql::File", Name = "a.sql", Type = CodeNodeType.File, ProjectContext = "SQL", FilePath = "a.sql" };
    private static SqlGraphSnapshot Snapshot(CodeNode node) => new()
    {
        ProjectContext = "SQL", Files = [new SqlFileGraph { Path = "a.sql", Status = "complete", Nodes = [node] }]
    };
}
