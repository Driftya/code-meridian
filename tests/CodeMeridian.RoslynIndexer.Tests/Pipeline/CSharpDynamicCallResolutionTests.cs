using CodeMeridian.RoslynIndexer.Pipeline;
using FluentAssertions;

namespace CodeMeridian.RoslynIndexer.Tests.Pipeline;

public sealed class CSharpDynamicCallResolutionTests
{
    [Theory]
    [InlineData("Dynamic", null)]
    [InlineData("TypedOrStatic", "dynamic")]
    public void DynamicReceiverIsIndeterminateEvenWhenALocalMethodHasTheSameName(string receiverKind, string? typeHint)
    {
        const string caller = "Project::Method::Example::Run()";
        const string target = "Project::Method::Example::Execute()";
        IngestNodeRequest[] nodes =
        [
            new(caller, "Run()", "Method", null, "Example.cs", 1, null),
            new(target, "Execute()", "Method", null, "Example.cs", 2, null)
        ];
        var properties = new Dictionary<string, string> { ["receiverKind"] = receiverKind };
        if (typeHint is not null) properties["receiverTypeHint"] = typeHint;
        IngestEdgeRequest[] edges = [new(caller, "", "Calls", CallName: "Execute", ParamCount: 0, Properties: properties)];

        var result = CSharpCallEdgeResolver.ResolveWithDiagnostics(nodes, edges);

        result.Edges.Should().BeEmpty();
        result.Stats.Indeterminate.Should().Be(1);
        result.Stats.ExternalOrUnindexed.Should().Be(0);
        result.Stats.Reasons.Should().Contain("indeterminate:dynamic_receiver", 1);
        result.Stats.HasValidAccounting.Should().BeTrue();
    }
}
