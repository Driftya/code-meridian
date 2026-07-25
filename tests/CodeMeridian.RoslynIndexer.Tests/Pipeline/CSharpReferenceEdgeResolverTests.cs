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

    [Fact]
    public void ResolveWithDiagnostics_SeparatesExternalAndSyntheticEdgesFromRawAccounting()
    {
        var interfaceId = "Project::Interface::Demo.IWorker";
        var implementationId = "Project::Class::Demo.Worker";
        var nodes = new List<IngestNodeRequest>
        {
            new(interfaceId, "IWorker", "Interface", "Demo", "src/IWorker.cs", 1, null),
            new(implementationId, "Worker", "Class", "Demo", "src/Worker.cs", 1, null),
            new("Project::Method::Demo.IWorker::Run()", "Run()", "Method", "Demo", "src/IWorker.cs", 2, null,
                Properties: new() { ["declaringTypeId"] = interfaceId }),
            new("Project::Method::Demo.Worker::Run()", "Run()", "Method", "Demo", "src/Worker.cs", 2, null,
                Properties: new() { ["declaringTypeId"] = implementationId })
        };
        var edges = new List<IngestEdgeRequest>
        {
            new(implementationId, interfaceId, "Implements"),
            new(implementationId, string.Empty, "Uses", TargetName: "CancellationToken", TargetType: "Class")
        };

        var result = CSharpReferenceEdgeResolver.ResolveWithDiagnostics(nodes, edges);

        result.Stats.Attempted.Should().Be(2);
        result.Stats.ResolvedLocal.Should().Be(1);
        result.Stats.ExternalOrUnindexed.Should().Be(1);
        result.Stats.SyntheticEdges.Should().Be(1);
        result.Stats.HasValidAccounting.Should().BeTrue();
        result.Edges.Should().Contain(edge =>
            edge.SourceId == "Project::Method::Demo.Worker::Run()"
            && edge.TargetId == "Project::Method::Demo.IWorker::Run()"
            && edge.RelationshipType == "Implements");
    }
}
