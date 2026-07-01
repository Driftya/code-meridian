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
            new("Project::Method::Demo.A.CallerClass::Caller()", "Caller()", "Method", "Demo.A", "src/A.cs", 10, null, Properties: new() { ["declaringTypeShortName"] = "CallerClass" }),
            new("Project::Method::Demo.A.CallerClass::Target()", "Target()", "Method", "Demo.A", "src/A.cs", 20, null, Properties: new() { ["declaringTypeShortName"] = "CallerClass" }),
            new("Project::Method::Demo.B.OtherClass::Target()", "Target()", "Method", "Demo.B", "src/B.cs", 20, null, Properties: new() { ["declaringTypeShortName"] = "OtherClass" })
        };
        var edges = new List<IngestEdgeRequest>
        {
            new("Project::Method::Demo.A.CallerClass::Caller()", string.Empty, "Calls", CallName: "Target", ParamCount: 0)
        };

        var result = CSharpCallEdgeResolver.Resolve(nodes, edges);

        result.Should().ContainSingle(edge =>
            edge.SourceId == "Project::Method::Demo.A.CallerClass::Caller()"
            && edge.TargetId == "Project::Method::Demo.A.CallerClass::Target()");
    }

    [Fact]
    public void Resolve_SelectsInterfaceCandidateFromReceiverTypeHint()
    {
        var nodes = new List<IngestNodeRequest>
        {
            new("Project::Method::Demo.Callers.ToolHost::Run()", "Run()", "Method", "Demo.Callers", "src/ToolHost.cs", 10, null, Properties: new() { ["declaringTypeShortName"] = "ToolHost" }),
            new("Project::Method::Demo.Contracts.ITool::Execute()", "Execute()", "Method", "Demo.Contracts", "src/ITool.cs", 5, null, Properties: new() { ["declaringTypeShortName"] = "ITool" }),
            new("Project::Method::Demo.Services.Tool::Execute()", "Execute()", "Method", "Demo.Services", "src/Tool.cs", 5, null, Properties: new() { ["declaringTypeShortName"] = "Tool" })
        };
        var edges = new List<IngestEdgeRequest>
        {
            new(
                "Project::Method::Demo.Callers.ToolHost::Run()",
                string.Empty,
                "Calls",
                CallName: "Execute",
                ParamCount: 0,
                Properties: new() { ["receiverTypeHint"] = "ITool" })
        };

        var result = CSharpCallEdgeResolver.Resolve(nodes, edges);

        result.Should().ContainSingle(edge =>
            edge.SourceId == "Project::Method::Demo.Callers.ToolHost::Run()"
            && edge.TargetId == "Project::Method::Demo.Contracts.ITool::Execute()");
    }

    [Fact]
    public void Resolve_AllowsOptionalParametersWhenInvocationUsesFewerArguments()
    {
        var nodes = new List<IngestNodeRequest>
        {
            new("Project::Method::Demo.Service::Caller()", "Caller()", "Method", "Demo", "src/Service.cs", 10, null, Properties: new() { ["declaringTypeShortName"] = "Service", ["requiredParameterCount"] = "0", ["totalParameterCount"] = "0" }),
            new("Project::Method::Demo.Service::Target(string)", "Target(string)", "Method", "Demo", "src/Service.cs", 20, null, Properties: new() { ["declaringTypeShortName"] = "Service", ["requiredParameterCount"] = "0", ["totalParameterCount"] = "1" })
        };
        var edges = new List<IngestEdgeRequest>
        {
            new("Project::Method::Demo.Service::Caller()", string.Empty, "Calls", CallName: "Target", ParamCount: 0)
        };

        var result = CSharpCallEdgeResolver.Resolve(nodes, edges);

        result.Should().ContainSingle(edge =>
            edge.SourceId == "Project::Method::Demo.Service::Caller()"
            && edge.TargetId == "Project::Method::Demo.Service::Target(string)");
    }
}
