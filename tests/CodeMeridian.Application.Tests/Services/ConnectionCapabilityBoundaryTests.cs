using CodeMeridian.Application.Projects;
using CodeMeridian.Application.Services;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;
using CodeMeridian.Core.Projects;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace CodeMeridian.Application.Tests.Services;

public sealed class ConnectionCapabilityBoundaryTests
{
    private readonly IProjectProfileRepository profiles = Substitute.For<IProjectProfileRepository>();
    private readonly ICodeGraphRepository graph = Substitute.For<ICodeGraphRepository>();
    private readonly CodeNode from = Node("From", "src/From.cs");
    private readonly CodeNode to = Node("To", "src/To.cs");

    [Theory]
    [InlineData("sql", "schema.sql")]
    [InlineData("powershell", "script.ps1")]
    [InlineData("html", "page.html")]
    [InlineData("other", "unknown.py")]
    public async Task CallableEndpointsCannotAuthorizeAnUnauditedIntermediateKind(string kind, string path)
    {
        var middle = Node("Middle", path);
        Setup(middle, kind);

        var result = await Service().FindConnectionResultAsync(from.Id, to.Id);

        result.Applicability!.Status.Should().Be("unknown");
        result.PathFound.Should().BeFalse();
        result.Nodes.Should().BeEmpty();
        result.Edges.Should().BeEmpty();
    }

    [Theory]
    [InlineData("absent")]
    [InlineData("source_less")]
    [InlineData("foreign")]
    [InlineData("wrong_identity")]
    public async Task UnverifiedIntermediateInventoryAndIdentitiesDiscardTheWholePath(string mode)
    {
        var middle = Node("Middle", mode == "source_less" ? null : "src/Middle.cs");
        if (mode == "foreign") middle = middle with { ProjectContext = "Other" };
        Setup(middle, "csharp");
        if (mode == "absent") profiles.GetAsync("Fixture", middle.FilePath, Arg.Any<CancellationToken>()).Returns(Stored(null));
        if (mode == "wrong_identity") graph.GetContextForEditingAsync(middle.Id, Arg.Any<CancellationToken>())
            .Returns(new EditingContext(middle with { Id = "Fixture::Method::Wrong" }, [], [], []));

        var result = await Service().FindConnectionResultAsync(from.Id, to.Id);

        result.Applicability!.Status.Should().Be("unknown");
        result.Nodes.Should().BeEmpty();
    }

    [Fact]
    public async Task VerifiedIntermediateCallableFactsRemainUsable()
    {
        var middle = Node("Middle", "src/Middle.cs");
        Setup(middle, "csharp");

        var result = await Service().FindConnectionResultAsync(from.Id, to.Id);

        result.Applicability!.Status.Should().Be("ok");
        result.Nodes.Select(node => node.Id).Should().Equal(from.Id, middle.Id, to.Id);
        result.HopCount.Should().Be(2);
    }

    [Fact]
    public async Task GenerationChangeDuringIntermediateValidationDiscardsThePath()
    {
        var middle = Node("Middle", "src/Middle.cs");
        Setup(middle, "csharp");
        profiles.GetAsync("Fixture", middle.FilePath, Arg.Any<CancellationToken>()).Returns(Stored(new(middle.FilePath!, "csharp")) with
        { Generation = 2, LatestGeneration = 2 });

        var result = await Service().FindConnectionResultAsync(from.Id, to.Id);

        result.Applicability!.Status.Should().Be("stale");
        result.Nodes.Should().BeEmpty();
    }

    [Fact]
    public async Task IntermediateEvidenceReadFailureIsExplicit()
    {
        var middle = Node("Middle", "src/Middle.cs");
        Setup(middle, "csharp");
        profiles.GetAsync("Fixture", middle.FilePath, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<StoredProjectProfile?>(new InvalidOperationException("unavailable")));

        var result = await Service().FindConnectionResultAsync(from.Id, to.Id);

        result.Applicability!.Status.Should().Be("failed");
        result.Nodes.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Reads")]
    [InlineData("JoinsWith")]
    [InlineData("UsesClass")]
    public async Task MatchingCallableEndpointsDoNotAuthorizeOtherRelationshipFamilies(string relationship)
    {
        var middle = Node("Middle", "src/Middle.cs");
        Setup(middle, "csharp");
        graph.FindConnectionAsync(from.Id, to.Id, Arg.Any<CancellationToken>()).Returns(
            [new(from, relationship, null), new(middle, "Calls", null), new(to, null, null)]);

        var result = await Service().FindConnectionResultAsync(from.Id, to.Id);

        result.Applicability!.Status.Should().Be("unknown");
        result.Edges.Should().BeEmpty();
    }

    [Fact]
    public async Task SqlNodeTypesCannotBorrowAValidCallableInventoryPath()
    {
        Setup(Node("Middle", "src/Middle.cs") with { Type = CodeNodeType.DatabaseFunction }, "csharp");

        var result = await Service().FindConnectionResultAsync(from.Id, to.Id);

        result.Applicability!.Status.Should().Be("unknown");
        result.Nodes.Should().BeEmpty();
    }

    [Fact]
    public async Task CancellationDuringSourceValidationPropagates()
    {
        var middle = Node("Middle", "src/Middle.cs");
        Setup(middle, "csharp");
        profiles.GetAsync("Fixture", middle.FilePath, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<StoredProjectProfile?>(new OperationCanceledException()));

        var query = () => Service().FindConnectionResultAsync(from.Id, to.Id);

        await query.Should().ThrowAsync<OperationCanceledException>();
    }

    private void Setup(CodeNode middle, string kind)
    {
        profiles.GetAsync("Fixture", Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var path = call.ArgAt<string?>(1);
            return Stored(path is null ? null : new(path, path == middle.FilePath ? kind : "csharp"));
        });
        foreach (var node in new[] { from, middle, to })
            graph.GetContextForEditingAsync(node.Id, Arg.Any<CancellationToken>()).Returns(new EditingContext(node, [], [], []));
        graph.FindConnectionAsync(from.Id, to.Id, Arg.Any<CancellationToken>()).Returns(
            [new(from, "Calls", null), new(middle, "Calls", null), new(to, null, null)]);
    }

    private static CodeNode Node(string name, string? path) => new()
    { Id = "Fixture::Method::" + name, Name = name, Type = CodeNodeType.Method, FilePath = path, ProjectContext = "Fixture" };

    private static StoredProjectProfile Stored(ProjectInventoryFile? target)
    {
        var profile = ToolCapabilityPolicyTests.Profile();
        return new(1, 1, DateTimeOffset.UtcNow, profile.Profile!, target) { AnalyzerOutcomes = profile.AnalyzerOutcomes };
    }

    private CodebaseQueryService Service() => new(graph, Substitute.For<IVectorRepository>(), new NoOpEmbeddingProvider(),
        Options.Create(new CodebaseAnalysisOptions()), Options.Create(new CodebaseIndexingOptions()),
        new DefaultAnalysisProfilePolicy(), new StaticProjectAnalysisOptionsResolver(new()), new ProjectProfileService(profiles));
}
