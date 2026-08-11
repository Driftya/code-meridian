using CodeMeridian.RoslynIndexer.Pipeline;
using FluentAssertions;

namespace CodeMeridian.RoslynIndexer.Tests.Pipeline;

public sealed class CSharpCallEdgeResolverTests
{
    [Fact]
    public void Resolve_PrefersTheCallingDeclaringTypeForUnqualifiedCalls()
    {
        var sourceId = "Project::Method::Demo.Service::Caller()";
        var expectedTargetId = "Project::Method::Demo.Service::Target()";
        var nodes = new List<IngestNodeRequest>
        {
            new(sourceId, "Caller()", "Method", "Demo", "src/Caller.cs", 10, null,
                Properties: new() { ["declaringTypeShortName"] = "Service" }),
            new(expectedTargetId, "Target()", "Method", "Demo", "src/Service.cs", 20, null,
                Properties: new() { ["declaringTypeShortName"] = "Service" }),
            new("Project::Method::Demo.OtherService::Target()", "Target()", "Method", "Demo", "src/OtherService.cs", 20, null,
                Properties: new() { ["declaringTypeShortName"] = "OtherService" })
        };
        var edges = new List<IngestEdgeRequest>
        {
            new(sourceId, string.Empty, "Calls", CallName: "Target", ParamCount: 0,
                Properties: new() { ["receiverKind"] = "Unqualified" })
        };

        var result = CSharpCallEdgeResolver.Resolve(nodes, edges);

        result.Should().ContainSingle(edge => edge.TargetId == expectedTargetId);
    }

    [Fact]
    public void Resolve_AllowsAdditionalArgumentsForParamsParameters()
    {
        var sourceId = "Project::Method::Demo.Service::Caller()";
        var targetId = "Project::Method::Demo.Service::Target(string[])";
        var nodes = new List<IngestNodeRequest>
        {
            new(sourceId, "Caller()", "Method", "Demo", "src/Service.cs", 10, null,
                Properties: new() { ["declaringTypeShortName"] = "Service" }),
            new(targetId, "Target(string[])", "Method", "Demo", "src/Service.cs", 20, null,
                Properties: new()
                {
                    ["declaringTypeShortName"] = "Service",
                    ["requiredParameterCount"] = "0",
                    ["totalParameterCount"] = "1",
                    ["hasParamsParameter"] = "True"
                })
        };
        var edges = new List<IngestEdgeRequest>
        {
            new(sourceId, string.Empty, "Calls", CallName: "Target", ParamCount: 3,
                Properties: new() { ["receiverKind"] = "Unqualified" })
        };

        var result = CSharpCallEdgeResolver.Resolve(nodes, edges);

        result.Should().ContainSingle(edge => edge.TargetId == targetId);
    }

    [Fact]
    public void Resolve_AdjustsExtensionReceiverAndParamsArityForInstanceSyntax()
    {
        var sourceId = "Project::Method::Demo.Host::Caller()";
        var targetId = "Project::Method::Demo.RunnerExtensions::RunAll(IRunner,string[])";
        var nodes = new List<IngestNodeRequest>
        {
            new(sourceId, "Caller()", "Method", "Demo", "src/Host.cs", 10, null,
                Properties: new() { ["declaringTypeShortName"] = "Host" }),
            new(targetId, "RunAll(IRunner,string[])", "Method", "Demo", "src/RunnerExtensions.cs", 20, null,
                Properties: new()
                {
                    ["declaringTypeShortName"] = "RunnerExtensions",
                    ["requiredParameterCount"] = "1",
                    ["totalParameterCount"] = "2",
                    ["hasParamsParameter"] = "True",
                    ["isExtensionMethod"] = "True",
                    ["extensionReceiverType"] = "IRunner"
                })
        };
        var edges = new List<IngestEdgeRequest>
        {
            new(sourceId, string.Empty, "Calls", CallName: "RunAll", ParamCount: 3,
                Properties: new()
                {
                    ["receiverKind"] = "TypedOrStatic",
                    ["receiverTypeHint"] = "IRunner"
                })
        };

        var result = CSharpCallEdgeResolver.Resolve(nodes, edges);

        result.Should().ContainSingle(edge => edge.TargetId == targetId);
    }

    [Fact]
    public void Resolve_UsesExplicitGenericArityToSelectAnOverload()
    {
        var sourceId = "Project::Method::Demo.Service::Caller()";
        var genericTargetId = "Project::Method::Demo.Service::Target(T)";
        var nodes = new List<IngestNodeRequest>
        {
            new(sourceId, "Caller()", "Method", "Demo", "src/Service.cs", 10, null,
                Properties: new() { ["declaringTypeShortName"] = "Service" }),
            new("Project::Method::Demo.Service::Target(string)", "Target(string)", "Method", "Demo", "src/Service.cs", 20, null,
                Properties: new()
                {
                    ["declaringTypeShortName"] = "Service",
                    ["requiredParameterCount"] = "1",
                    ["totalParameterCount"] = "1",
                    ["genericParameterCount"] = "0"
                }),
            new(genericTargetId, "Target(T)", "Method", "Demo", "src/Service.cs", 25, null,
                Properties: new()
                {
                    ["declaringTypeShortName"] = "Service",
                    ["requiredParameterCount"] = "1",
                    ["totalParameterCount"] = "1",
                    ["genericParameterCount"] = "1"
                })
        };
        var edges = new List<IngestEdgeRequest>
        {
            new(sourceId, string.Empty, "Calls", CallName: "Target", ParamCount: 1,
                Properties: new()
                {
                    ["receiverKind"] = "Unqualified",
                    ["genericArity"] = "1"
                })
        };

        var result = CSharpCallEdgeResolver.Resolve(nodes, edges);

        result.Should().ContainSingle(edge => edge.TargetId == genericTargetId);
    }

    [Fact]
    public void Resolve_SelectsAMemberDeclaredOnAnExactLocalBaseType()
    {
        var sourceId = "Project::Method::Demo.Host::Caller()";
        var baseTargetId = "Project::Method::Demo.BaseHost::Target()";
        var nodes = new List<IngestNodeRequest>
        {
            new("Project::Class::Demo.Host", "Host", "Class", "Demo", "src/Host.cs", 1, null),
            new("Project::Class::Demo.BaseHost", "BaseHost", "Class", "Demo", "src/BaseHost.cs", 1, null),
            new("Project::Class::Demo.OtherHost", "OtherHost", "Class", "Demo", "src/OtherHost.cs", 1, null),
            new(sourceId, "Caller()", "Method", "Demo", "src/Host.cs", 10, null,
                Properties: new()
                {
                    ["declaringTypeId"] = "Project::Class::Demo.Host",
                    ["declaringTypeShortName"] = "Host"
                }),
            new(baseTargetId, "Target()", "Method", "Demo", "src/BaseHost.cs", 20, null,
                Properties: new()
                {
                    ["declaringTypeId"] = "Project::Class::Demo.BaseHost",
                    ["declaringTypeShortName"] = "BaseHost"
                }),
            new("Project::Method::Demo.OtherHost::Target()", "Target()", "Method", "Demo", "src/OtherHost.cs", 20, null,
                Properties: new()
                {
                    ["declaringTypeId"] = "Project::Class::Demo.OtherHost",
                    ["declaringTypeShortName"] = "OtherHost"
                })
        };
        var edges = new List<IngestEdgeRequest>
        {
            new("Project::Class::Demo.Host", "Project::Class::BaseHost", "Inherits",
                TargetName: "BaseHost", TargetType: "Class"),
            new(sourceId, string.Empty, "Calls", CallName: "Target", ParamCount: 0,
                Properties: new()
                {
                    ["receiverKind"] = "ThisOrBase",
                    ["receiverTypeHint"] = "Host"
                })
        };

        var result = CSharpCallEdgeResolver.Resolve(nodes, edges);

        result.Should().ContainSingle(edge => edge.RelationshipType == "Calls" && edge.TargetId == baseTargetId);
    }

    [Fact]
    public void Resolve_DoesNotUseAUniqueUnrelatedMethodForThisReceiver()
    {
        var sourceId = "Project::Method::Demo.Host::Caller()";
        var nodes = new List<IngestNodeRequest>
        {
            new("Project::Class::Demo.Host", "Host", "Class", "Demo", "src/Host.cs", 1, null),
            new(sourceId, "Caller()", "Method", "Demo", "src/Host.cs", 10, null,
                Properties: new()
                {
                    ["declaringTypeId"] = "Project::Class::Demo.Host",
                    ["declaringTypeShortName"] = "Host"
                }),
            new("Project::Method::Demo.OtherHost::Target()", "Target()", "Method", "Demo", "src/OtherHost.cs", 20, null,
                Properties: new()
                {
                    ["declaringTypeId"] = "Project::Class::Demo.OtherHost",
                    ["declaringTypeShortName"] = "OtherHost"
                })
        };
        var edges = new List<IngestEdgeRequest>
        {
            new(sourceId, string.Empty, "Calls", CallName: "Target", ParamCount: 0,
                Properties: new()
                {
                    ["receiverKind"] = "ThisOrBase",
                    ["receiverTypeHint"] = "Host"
                })
        };

        var result = CSharpCallEdgeResolver.ResolveWithDiagnostics(nodes, edges);

        result.Edges.Should().BeEmpty();
        result.UnresolvedByReason.Should().Contain("ambiguous_local_target", 1);
    }

    [Fact]
    public void Resolve_ClassifiesPossibleExternalBaseMemberAsIndeterminate()
    {
        var sourceId = "Project::Method::Demo.Factory::Caller()";
        var nodes = new List<IngestNodeRequest>
        {
            new("Project::Class::Demo.Factory", "Factory", "Class", "Demo", "src/Factory.cs", 1, null),
            new(sourceId, "Caller()", "Method", "Demo", "src/Factory.cs", 10, null,
                Properties: new()
                {
                    ["declaringTypeId"] = "Project::Class::Demo.Factory",
                    ["declaringTypeShortName"] = "Factory"
                })
        };
        var edges = new List<IngestEdgeRequest>
        {
            new("Project::Class::Demo.Factory", "Project::Class::ExternalFactory", "Inherits",
                TargetName: "ExternalFactory", TargetType: "Class"),
            new(sourceId, string.Empty, "Calls", CallName: "CreateClient", ParamCount: 0,
                Properties: new()
                {
                    ["receiverKind"] = "ThisOrBase",
                    ["receiverTypeHint"] = "Factory"
                })
        };

        var result = CSharpCallEdgeResolver.ResolveWithDiagnostics(nodes, edges);

        result.Edges.Should().ContainSingle(edge => edge.RelationshipType == "Inherits");
        result.UnresolvedByReason.Should().Contain("external_base_member_possible", 1);
        result.Stats.Indeterminate.Should().Be(1);
        result.Stats.UnresolvedLocal.Should().Be(0);
    }

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

    [Fact]
    public void Resolve_UsesConventionalTestClassSubjectWhenReceiverTypeIsUnavailable()
    {
        var nodes = new List<IngestNodeRequest>
        {
            new("Project::Method::Demo.Tests.CodebaseQueryServiceFindCoverageGapsTests::ReportsGap()", "ReportsGap()", "Method", "Demo.Tests", "tests/CodebaseQueryServiceFindCoverageGapsTests.cs", 10, null, Properties: new() { ["declaringTypeShortName"] = "CodebaseQueryServiceFindCoverageGapsTests" }),
            new("Project::Method::Demo.Services.CodebaseQueryService::FindCoverageGapsAsync(string)", "FindCoverageGapsAsync(string)", "Method", "Demo.Services", "src/CodebaseQueryService.cs", 20, null, Properties: new() { ["declaringTypeShortName"] = "CodebaseQueryService" }),
            new("Project::Method::Demo.Contracts.ICodebaseQueryService::FindCoverageGapsAsync(string)", "FindCoverageGapsAsync(string)", "Method", "Demo.Contracts", "src/ICodebaseQueryService.cs", 5, null, Properties: new() { ["declaringTypeShortName"] = "ICodebaseQueryService" }),
            new("Project::Method::Demo.Tools.CodebaseTools::FindCoverageGapsAsync(string)", "FindCoverageGapsAsync(string)", "Method", "Demo.Tools", "src/CodebaseTools.cs", 5, null, Properties: new() { ["declaringTypeShortName"] = "CodebaseTools" })
        };
        var edges = new List<IngestEdgeRequest>
        {
            new(
                "Project::Method::Demo.Tests.CodebaseQueryServiceFindCoverageGapsTests::ReportsGap()",
                string.Empty,
                "Calls",
                CallName: "FindCoverageGapsAsync",
                ParamCount: 1)
        };

        var result = CSharpCallEdgeResolver.Resolve(nodes, edges);

        result.Should().ContainSingle(edge =>
            edge.TargetId == "Project::Method::Demo.Services.CodebaseQueryService::FindCoverageGapsAsync(string)");
    }

    [Fact]
    public void Resolve_UsesConventionalTestClassSubjectForUnknownInheritedMemberReceiver()
    {
        var sourceId = "Project::Method::Demo.Tests.Neo4jCodeGraphRepositoryDeleteDiagnosticsIntegrationTests::PreservesMetadata()";
        var nodes = new List<IngestNodeRequest>
        {
            new(sourceId, "PreservesMetadata()", "Method", "Demo.Tests", "tests/Neo4jCodeGraphRepositoryDeleteDiagnosticsIntegrationTests.cs", 10, null,
                Properties: new() { ["declaringTypeShortName"] = "Neo4jCodeGraphRepositoryDeleteDiagnosticsIntegrationTests" }),
            new("Project::Method::Demo.Contracts.ICodeGraphRepository::DeleteDiagnosticsAsync(string,CancellationToken)", "DeleteDiagnosticsAsync(string,CancellationToken)", "Method", "Demo.Contracts", "src/ICodeGraphRepository.cs", 5, null,
                Properties: new() { ["declaringTypeShortName"] = "ICodeGraphRepository", ["requiredParameterCount"] = "1", ["totalParameterCount"] = "2" }),
            new("Project::Method::Demo.Graph.Neo4jCodeGraphRepository::DeleteDiagnosticsAsync(string,CancellationToken)", "DeleteDiagnosticsAsync(string,CancellationToken)", "Method", "Demo.Graph", "src/Neo4jCodeGraphRepository.cs", 20, null,
                Properties: new() { ["declaringTypeShortName"] = "Neo4jCodeGraphRepository", ["requiredParameterCount"] = "1", ["totalParameterCount"] = "2" })
        };
        var edges = new List<IngestEdgeRequest>
        {
            new(
                sourceId,
                string.Empty,
                "Calls",
                CallName: "DeleteDiagnosticsAsync",
                ParamCount: 1,
                Properties: new() { ["receiverKind"] = "UnknownMember" })
        };

        var result = CSharpCallEdgeResolver.ResolveWithDiagnostics(nodes, edges);

        result.Edges.Should().ContainSingle(edge =>
            edge.TargetId == "Project::Method::Demo.Graph.Neo4jCodeGraphRepository::DeleteDiagnosticsAsync(string,CancellationToken)");
        result.UnresolvedByReason.Should().BeEmpty();
    }

    [Theory]
    [InlineData("RunAsync", "Project::Method::Demo.IndexWatchLoop::RunAsync(string)")]
    [InlineData("Add", "Project::Method::Demo.EdgeResolutionResult::Add(string)")]
    public void ResolveWithDiagnostics_DoesNotResolveUnknownMemberReceiverToUniqueLocalCandidate(
        string callName,
        string candidateId)
    {
        var sourceId = "Project::Method::Demo.Repository::DeleteDiagnosticsAsync()";
        var nodes = new List<IngestNodeRequest>
        {
            new(sourceId, "DeleteDiagnosticsAsync()", "Method", "Demo", "src/Repository.cs", 10, null,
                Properties: new() { ["declaringTypeShortName"] = "Repository" }),
            new(candidateId, $"{callName}(string)", "Method", "Demo", "src/Other.cs", 20, null,
                Properties: new() { ["declaringTypeShortName"] = callName == "Add" ? "EdgeResolutionResult" : "IndexWatchLoop" })
        };
        var edges = new List<IngestEdgeRequest>
        {
            new(sourceId, string.Empty, "Calls", CallName: callName, ParamCount: 1,
                Properties: new() { ["receiverKind"] = "UnknownMember" })
        };

        var result = CSharpCallEdgeResolver.ResolveWithDiagnostics(nodes, edges);

        result.Edges.Should().BeEmpty();
        result.UnresolvedByReason.Should().ContainSingle()
            .Which.Should().Be(new KeyValuePair<string, int>("unknown_member_receiver", 1));
    }

    [Fact]
    public void ResolveWithDiagnostics_DoesNotFallbackWhenTypedReceiverHasNoMatchingLocalType()
    {
        var sourceId = "Project::Method::Demo.Repository::DeleteDiagnosticsAsync()";
        var nodes = new List<IngestNodeRequest>
        {
            new(sourceId, "DeleteDiagnosticsAsync()", "Method", "Demo", "src/Repository.cs", 10, null,
                Properties: new() { ["declaringTypeShortName"] = "Repository" }),
            new("Project::Method::Demo.IndexWatchLoop::RunAsync(string)", "RunAsync(string)", "Method", "Demo", "src/Other.cs", 20, null,
                Properties: new() { ["declaringTypeShortName"] = "IndexWatchLoop" })
        };
        var edges = new List<IngestEdgeRequest>
        {
            new(sourceId, string.Empty, "Calls", CallName: "RunAsync", ParamCount: 1,
                Properties: new() { ["receiverKind"] = "TypedOrStatic", ["receiverTypeHint"] = "IAsyncQueryRunner" })
        };

        var result = CSharpCallEdgeResolver.ResolveWithDiagnostics(nodes, edges);

        result.Edges.Should().BeEmpty();
        result.Stats.ExternalOrUnindexed.Should().Be(1);
        result.Stats.UnresolvedLocal.Should().Be(0);
        result.Stats.HasValidAccounting.Should().BeTrue();
    }

    [Fact]
    public void ResolveWithDiagnostics_ClassifiesUnrelatedInstanceNameCollisionAsPossibleExternalExtension()
    {
        var sourceId = "Project::Method::Demo.OrderService::BuildLookup()";
        var nodes = new List<IngestNodeRequest>
        {
            new("Project::Class::Demo.OrderCollection", "OrderCollection", "Class", "Demo", "src/OrderCollection.cs", 1, null),
            new(sourceId, "BuildLookup()", "Method", "Demo", "src/OrderService.cs", 10, null,
                Properties: new() { ["declaringTypeShortName"] = "OrderService" }),
            new("Project::Method::Demo.MappingHelpers::ToDictionary(string)", "ToDictionary(string)", "Method", "Demo", "src/MappingHelpers.cs", 20, null,
                Properties: new()
                {
                    ["declaringTypeShortName"] = "MappingHelpers",
                    ["requiredParameterCount"] = "1",
                    ["totalParameterCount"] = "1",
                    ["parameterMetadata"] = "exact-syntax"
                })
        };
        var edges = new List<IngestEdgeRequest>
        {
            new(sourceId, string.Empty, "Calls", CallName: "ToDictionary", ParamCount: 2,
                Properties: new()
                {
                    ["receiverKind"] = "TypedOrStatic",
                    ["receiverTypeHint"] = "OrderCollection",
                    ["receiverEvidenceSource"] = "syntax-parameter"
                })
        };

        var result = CSharpCallEdgeResolver.ResolveWithDiagnostics(nodes, edges);

        result.Edges.Should().BeEmpty();
        result.Stats.ExternalOrUnindexed.Should().Be(1);
        result.Stats.UnresolvedLocal.Should().Be(0);
        result.Stats.Reasons.Should().Contain("external_or_unindexed:external_extension_possible", 1);
        result.Stats.HasValidAccounting.Should().BeTrue();
    }

    [Fact]
    public void ResolveWithDiagnostics_KeepsRelatedInstanceArityMismatchLocal()
    {
        var sourceId = "Project::Method::Demo.OrderService::BuildLookup()";
        var nodes = new List<IngestNodeRequest>
        {
            new("Project::Class::Demo.OrderCollection", "OrderCollection", "Class", "Demo", "src/OrderCollection.cs", 1, null),
            new(sourceId, "BuildLookup()", "Method", "Demo", "src/OrderService.cs", 10, null,
                Properties: new() { ["declaringTypeShortName"] = "OrderService" }),
            new("Project::Method::Demo.OrderCollection::ToDictionary(string)", "ToDictionary(string)", "Method", "Demo", "src/OrderCollection.cs", 20, null,
                Properties: new()
                {
                    ["declaringTypeId"] = "Project::Class::Demo.OrderCollection",
                    ["declaringTypeShortName"] = "OrderCollection",
                    ["requiredParameterCount"] = "1",
                    ["totalParameterCount"] = "1",
                    ["parameterMetadata"] = "exact-syntax"
                })
        };
        var edges = new List<IngestEdgeRequest>
        {
            new(sourceId, string.Empty, "Calls", CallName: "ToDictionary", ParamCount: 2,
                Properties: new()
                {
                    ["receiverKind"] = "TypedOrStatic",
                    ["receiverTypeHint"] = "OrderCollection",
                    ["receiverEvidenceSource"] = "syntax-parameter"
                })
        };

        var result = CSharpCallEdgeResolver.ResolveWithDiagnostics(nodes, edges);

        result.Edges.Should().BeEmpty();
        result.Stats.ExternalOrUnindexed.Should().Be(0);
        result.Stats.UnresolvedLocal.Should().Be(1);
        result.Stats.Reasons.Should().Contain("unresolved_local:local_target_incompatible_arity", 1);
        result.Stats.HasValidAccounting.Should().BeTrue();
    }

    [Fact]
    public void ResolveWithDiagnostics_DuplicateResolvedCallsPreserveCandidateAccounting()
    {
        var sourceId = "Project::Method::Demo.Service::Caller()";
        var targetId = "Project::Method::Demo.Service::Target()";
        var nodes = new List<IngestNodeRequest>
        {
            new(sourceId, "Caller()", "Method", "Demo", "src/Service.cs", 10, null,
                Properties: new() { ["declaringTypeShortName"] = "Service" }),
            new(targetId, "Target()", "Method", "Demo", "src/Service.cs", 20, null,
                Properties: new() { ["declaringTypeShortName"] = "Service" })
        };
        var edges = new List<IngestEdgeRequest>
        {
            new(sourceId, string.Empty, "Calls", CallName: "Target", ParamCount: 0),
            new(sourceId, string.Empty, "Calls", CallName: "Target", ParamCount: 0)
        };

        var result = CSharpCallEdgeResolver.ResolveWithDiagnostics(nodes, edges);

        result.Edges.Should().ContainSingle();
        result.Stats.Attempted.Should().Be(2);
        result.Stats.ResolvedLocal.Should().Be(2);
        result.Stats.DuplicateEdges.Should().Be(1);
        result.UniqueResolvedEdges.Should().Be(1);
        result.Stats.HasValidAccounting.Should().BeTrue();
    }

    [Fact]
    public void ResolveWithDiagnostics_MissingCallMetadata_IsIndeterminateAndNotPersisted()
    {
        var sourceId = "Project::Method::Demo.Service::Caller()";
        var nodes = new List<IngestNodeRequest>
        {
            new(sourceId, "Caller()", "Method", "Demo", "src/Service.cs", 10, null)
        };
        var edges = new List<IngestEdgeRequest>
        {
            new(sourceId, string.Empty, "Calls", CallName: null, ParamCount: 0)
        };

        var result = CSharpCallEdgeResolver.ResolveWithDiagnostics(nodes, edges);

        result.Edges.Should().BeEmpty();
        result.Stats.Indeterminate.Should().Be(1);
        result.Stats.Reasons.Should().ContainKey("indeterminate:missing_call_name");
        result.Stats.HasValidAccounting.Should().BeTrue();
    }
}
