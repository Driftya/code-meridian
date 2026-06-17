using CodeMeridian.RoslynIndexer.Pipeline;
using FluentAssertions;

namespace CodeMeridian.RoslynIndexer.Tests.Pipeline;

public sealed class CSharpReferenceEdgeResolverTests
{
    [Fact]
    public void Resolve_SelectsTypeCandidateByFileWhenNamespaceDiffers()
    {
        var nodes = new List<IngestNodeRequest>
        {
            new("Project::Class::Demo.A.Source", "Source", "Class", "Demo.A", "src/A.cs", 10, null),
            new("Project::Class::Demo.A.SharedType", "SharedType", "Class", "Demo.A", "src/A.cs", 20, null),
            new("Project::Class::Demo.B.SharedType", "SharedType", "Class", "Demo.B", "src/B.cs", 20, null)
        };
        var edges = new List<IngestEdgeRequest>
        {
            new(
                "Project::Class::Demo.A.Source",
                string.Empty,
                "Uses",
                TargetName: "SharedType",
                TargetType: "Class")
        };

        var result = CSharpReferenceEdgeResolver.Resolve(nodes, edges);

        result.Should().ContainSingle(edge =>
            edge.SourceId == "Project::Class::Demo.A.Source"
            && edge.TargetId == "Project::Class::Demo.A.SharedType");
    }

    [Fact]
    public void Resolve_FallsBackToTypeNameForClassAndStructTargets()
    {
        var nodes = new List<IngestNodeRequest>
        {
            new("Project::Struct::Demo.Box", "Box", "Struct", "Demo", "src/Box.cs", 10, null),
            new("Project::Struct::Demo.Size", "Size", "Struct", "Demo", "src/Size.cs", 10, null),
            new("Project::Class::Demo.Point", "Point", "Class", "Demo", "src/Point.cs", 10, null)
        };
        var edges = new List<IngestEdgeRequest>
        {
            new("Project::Struct::Demo.Box", string.Empty, "Uses", TargetName: "Size", TargetType: "Class"),
            new("Project::Struct::Demo.Box", string.Empty, "Uses", TargetName: "Point", TargetType: "Class")
        };

        var result = CSharpReferenceEdgeResolver.Resolve(nodes, edges);

        result.Should().Contain(edge =>
            edge.SourceId == "Project::Struct::Demo.Box"
            && edge.TargetId == "Project::Struct::Demo.Size");
        result.Should().Contain(edge =>
            edge.SourceId == "Project::Struct::Demo.Box"
            && edge.TargetId == "Project::Class::Demo.Point");
    }
}
