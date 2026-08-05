using CodeMeridian.RoslynIndexer.Pipeline;
using FluentAssertions;

namespace CodeMeridian.RoslynIndexer.Tests.Pipeline;

public sealed class CSharpCallEdgeResolverCompletenessTests
{
    [Fact]
    public void ResolveWithDiagnostics_UnqualifiedReceiverHint_MatchesQualifiedCandidateByShortName()
    {
        var source = Method(
            "Project::Method::Consumer.Run()",
            "Run()",
            "Consumer",
            "Consumer");
        var target = Method(
            "Project::Method::Feature.Widget.Save()",
            "Save()",
            "Widget",
            "Feature.Widget");
        var edge = Call(source.Id, "Save", "TypedOrStatic", "Widget", "Widget");

        var result = CSharpCallEdgeResolver.ResolveWithDiagnostics([source, target], [edge]);

        result.Edges.Should().ContainSingle().Which.TargetId.Should().Be(target.Id);
        result.Stats.UnresolvedLocal.Should().Be(0);
    }

    [Fact]
    public void ResolveWithDiagnostics_ClassifiesExternalChainRootsWithoutCreatingLocalEdges()
    {
        var sourceId = "Project::Method::Demo.Host::Configure(IServiceCollection)";
        var nodes = new List<IngestNodeRequest>
        {
            Method(sourceId, "Configure(IServiceCollection)", "Host", "Demo.Host"),
            Method("Project::Method::Demo.Extensions::AddSecond(IServiceCollection)", "AddSecond(IServiceCollection)", "Extensions", "Demo.Extensions")
        };
        var edges = new List<IngestEdgeRequest>
        {
            Call(sourceId, "AddSecond", "Chained", "IServiceCollection", "Microsoft.Extensions.DependencyInjection.IServiceCollection")
        };

        var result = CSharpCallEdgeResolver.ResolveWithDiagnostics(nodes, edges);

        result.Edges.Should().BeEmpty();
        result.Stats.ExternalOrUnindexed.Should().Be(1);
        result.Stats.Reasons.Should().Contain("external_or_unindexed:external_chain_root", 1);
        result.Stats.HasValidAccounting.Should().BeTrue();
    }

    [Fact]
    public void ResolveWithDiagnostics_KeepsLocalChainReturnTypesIndeterminate()
    {
        var sourceId = "Project::Method::Demo.Host::Configure(LocalBuilder)";
        var nodes = new List<IngestNodeRequest>
        {
            new("Project::Class::Demo.LocalBuilder", "LocalBuilder", "Class", "Demo", "src/LocalBuilder.cs", 1, null),
            Method(sourceId, "Configure(LocalBuilder)", "Host", "Demo.Host"),
            Method("Project::Method::Demo.LocalBuilder::Finish()", "Finish()", "LocalBuilder", "Demo.LocalBuilder")
        };
        var edges = new List<IngestEdgeRequest>
        {
            Call(sourceId, "Finish", "Chained", "LocalBuilder", "Demo.LocalBuilder")
        };

        var result = CSharpCallEdgeResolver.ResolveWithDiagnostics(nodes, edges);

        result.Edges.Should().BeEmpty();
        result.Stats.Indeterminate.Should().Be(1);
        result.Stats.Reasons.Should().Contain("indeterminate:chained_receiver_return_unknown", 1);
        result.Stats.HasValidAccounting.Should().BeTrue();
    }

    [Fact]
    public void Resolve_UsesCanonicalReceiverIdentityWhenShortTypeNamesCollide()
    {
        var sourceId = "Project::Method::Demo.Host::Execute()";
        var expectedTarget = "Project::Method::Alpha.Service::Run()";
        var nodes = new List<IngestNodeRequest>
        {
            Method(sourceId, "Execute()", "Host", "Demo.Host"),
            Method(expectedTarget, "Run()", "Service", "Alpha.Service"),
            Method("Project::Method::Beta.Service::Run()", "Run()", "Service", "Beta.Service")
        };
        var edges = new List<IngestEdgeRequest>
        {
            Call(sourceId, "Run", "TypedOrStatic", "Service", "Alpha.Service")
        };

        var result = CSharpCallEdgeResolver.Resolve(nodes, edges);

        result.Should().ContainSingle(edge => edge.TargetId == expectedTarget);
    }

    [Fact]
    public void ResolveWithDiagnostics_SeparatesMissingArityMetadataFromKnownIncompatibility()
    {
        var sourceId = "Project::Method::Demo.Host::Execute()";
        var nodes = new List<IngestNodeRequest>
        {
            Method(sourceId, "Execute()", "Host", "Demo.Host"),
            new(
                "Project::Method::Demo.Host::Run(string)",
                "Run(string)",
                "Method",
                "Demo",
                "src/Host.cs",
                20,
                null,
                Properties: new()
                {
                    ["declaringTypeShortName"] = "Host",
                    ["declaringTypeCanonicalName"] = "Demo.Host"
                })
        };
        var edges = new List<IngestEdgeRequest>
        {
            Call(sourceId, "Run", "Unqualified", parameterCount: 2)
        };

        var result = CSharpCallEdgeResolver.ResolveWithDiagnostics(nodes, edges);

        result.Edges.Should().BeEmpty();
        result.Stats.Indeterminate.Should().Be(1);
        result.Stats.Reasons.Should().Contain("indeterminate:insufficient_arity_metadata", 1);
    }

    private static IngestNodeRequest Method(
        string id,
        string name,
        string declaringType,
        string canonicalDeclaringType) =>
        new(
            id,
            name,
            "Method",
            canonicalDeclaringType.Contains('.') ? canonicalDeclaringType[..canonicalDeclaringType.LastIndexOf('.')] : null,
            $"src/{declaringType}.cs",
            10,
            null,
            Properties: new()
            {
                ["declaringTypeShortName"] = declaringType,
                ["declaringTypeCanonicalName"] = canonicalDeclaringType,
                ["requiredParameterCount"] = "0",
                ["totalParameterCount"] = "0",
                ["parameterMetadata"] = "exact-syntax"
            });

    private static IngestEdgeRequest Call(
        string sourceId,
        string name,
        string receiverKind,
        string? receiverType = null,
        string? canonicalReceiverType = null,
        int parameterCount = 0)
    {
        var properties = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["receiverKind"] = receiverKind,
            ["parameterMetadata"] = "exact-syntax"
        };
        if (receiverType is not null)
            properties["receiverTypeHint"] = receiverType;
        if (canonicalReceiverType is not null)
            properties["receiverCanonicalTypeHint"] = canonicalReceiverType;

        return new IngestEdgeRequest(
            sourceId,
            string.Empty,
            "Calls",
            CallName: name,
            ParamCount: parameterCount,
            Properties: properties);
    }
}
