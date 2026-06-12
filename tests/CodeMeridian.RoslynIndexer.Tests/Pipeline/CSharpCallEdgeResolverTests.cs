using CodeMeridian.RoslynIndexer.Pipeline;
using FluentAssertions;

namespace CodeMeridian.RoslynIndexer.Tests.Pipeline;

public sealed class CSharpCallEdgeResolverTests
{
    [Fact]
    public void Resolve_SelectsBestCandidateByNamespaceWhenFileDiffers()
    {
        var nodes = new List<IngestNodeRequest>
        {
            new("Project::Method::Demo.A.Caller()", "Caller()", "Method", "Demo.A", "src/A.cs", 10, null),
            new("Project::Method::Demo.A.Target()", "Target()", "Method", "Demo.A", "src/A.cs", 20, null),
            new("Project::Method::Demo.B.Target()", "Target()", "Method", "Demo.B", "src/B.cs", 20, null)
        };
        var edges = new List<IngestEdgeRequest>
        {
            new("Project::Method::Demo.A.Caller()", string.Empty, "Calls", CallName: "Target", ParamCount: 0)
        };

        var result = CSharpCallEdgeResolver.Resolve(nodes, edges);

        result.Should().ContainSingle(edge =>
            edge.SourceId == "Project::Method::Demo.A.Caller()"
            && edge.TargetId == "Project::Method::Demo.A.Target()");
    }
}
