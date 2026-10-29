using CodeMeridian.Application.Projects;
using CodeMeridian.Application.Services;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;
using CodeMeridian.Core.Projects;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace CodeMeridian.Application.Tests.Services;

public sealed class CallableResultBoundaryTests
{
    private readonly ICodeGraphRepository graph = Substitute.For<ICodeGraphRepository>();
    private readonly IProjectProfileRepository profiles = Substitute.For<IProjectProfileRepository>();
    private readonly CodeNode target = Node("Target", "src/A.cs");

    [Theory]
    [InlineData("impact", "sql", "schema.sql")]
    [InlineData("impact", "html", "page.html")]
    [InlineData("impact", "powershell", "script.ps1")]
    [InlineData("context", "sql", "schema.sql")]
    [InlineData("context", "html", "page.html")]
    [InlineData("context", "powershell", "script.ps1")]
    public async Task RelatedSourcesCannotBorrowCallableTargetReadiness(string query, string kind, string path)
    {
        Setup(Node("Related", path), kind);

        var (status, output) = await Query(query);

        status.Should().Be("unknown");
        output.Should().NotContain("secret-source").And.NotContain("`Related`");
    }

    [Theory]
    [InlineData("impact", "missing")]
    [InlineData("impact", "identity")]
    [InlineData("impact", "type")]
    [InlineData("impact", "source_less")]
    [InlineData("context", "missing")]
    [InlineData("context", "identity")]
    [InlineData("context", "type")]
    [InlineData("context", "source_less")]
    public async Task UnverifiedRelatedNodesDiscardResultsAndSnippets(string query, string mode)
    {
        var related = Node("Related", mode == "source_less" ? null : "src/Related.cs");
        Setup(related);
        if (mode == "missing") profiles.GetAsync("Fixture", related.FilePath, Arg.Any<CancellationToken>()).Returns(Stored(null));
        if (mode == "identity") graph.GetContextForEditingAsync(related.Id, Arg.Any<CancellationToken>())
            .Returns(new EditingContext(related with { Id = "wrong" }, [], [], []));
        if (mode == "type") graph.GetContextForEditingAsync(related.Id, Arg.Any<CancellationToken>())
            .Returns(new EditingContext(related with { Type = CodeNodeType.DatabaseFunction }, [], [], []));

        var (status, output) = await Query(query);

        status.Should().Be("unknown");
        output.Should().NotContain("secret-source");
    }

    [Theory]
    [InlineData("impact")]
    [InlineData("context")]
    public async Task VerifiedCallableRelatedNodesRemainUsable(string query)
    {
        Setup(Node("Related", "src/Related.cs"));

        var (status, output) = await Query(query);

        status.Should().Be("ok");
        output.Should().Contain("Related");
    }

    [Theory]
    [InlineData("impact", "generation")]
    [InlineData("context", "generation")]
    [InlineData("impact", "failure")]
    [InlineData("context", "failure")]
    public async Task ChangedOrUnreadableRelatedEvidenceCannotExposeResults(string query, string mode)
    {
        var related = Node("Related", "src/Related.cs");
        Setup(related);
        if (mode == "generation") profiles.GetAsync("Fixture", related.FilePath, Arg.Any<CancellationToken>())
            .Returns(Stored(new(related.FilePath!, "csharp")) with { Generation = 2, LatestGeneration = 2 });
        else profiles.GetAsync("Fixture", related.FilePath, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<StoredProjectProfile?>(new IOException("fixture")));

        var (status, output) = await Query(query);

        status.Should().Be(mode == "generation" ? "stale" : "failed");
        output.Should().NotContain("secret-source");
    }

    [Theory]
    [InlineData("impact")]
    [InlineData("context")]
    public async Task RelatedEvidenceCancellationPropagates(string query)
    {
        var related = Node("Related", "src/Related.cs");
        Setup(related);
        profiles.GetAsync("Fixture", related.FilePath, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<StoredProjectProfile?>(new OperationCanceledException()));

        await FluentActions.Awaiting(() => Query(query)).Should().ThrowAsync<OperationCanceledException>();
    }

    private void Setup(CodeNode related, string kind = "csharp")
    {
        profiles.GetAsync("Fixture", Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(call =>
            Stored(new(call.ArgAt<string>(1) ?? target.FilePath!, "csharp")));
        if (related.FilePath is not null)
            profiles.GetAsync("Fixture", related.FilePath, Arg.Any<CancellationToken>()).Returns(Stored(new(related.FilePath, kind)));
        graph.GetContextForEditingAsync(target.Id, Arg.Any<CancellationToken>()).Returns(new EditingContext(target, [related], [], []));
        graph.GetContextForEditingAsync(related.Id, Arg.Any<CancellationToken>()).Returns(new EditingContext(related, [], [], []));
        graph.FindImpactAsync(target.Id, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([(related, 1)]);
    }

    private async Task<(string?, string)> Query(string query)
    {
        var service = new CodebaseQueryService(graph, Substitute.For<IVectorRepository>(), new NoOpEmbeddingProvider(),
            Options.Create(new CodebaseAnalysisOptions()), Options.Create(new CodebaseIndexingOptions()),
            new DefaultAnalysisProfilePolicy(), new StaticProjectAnalysisOptionsResolver(new()), new ProjectProfileService(profiles));
        if (query == "impact")
        {
            var result = await service.FindImpactResultAsync(target.Id);
            return (result.Applicability?.Status, result.ToMarkdown(ContextDetailLevel.Full, false));
        }
        var context = await service.BuildMinimalContextResultAsync(target.Id, includeTests: false, includeSourceSnippets: true);
        return (context.Applicability?.Status, context.ToMarkdown(ContextDetailLevel.Full));
    }

    private static CodeNode Node(string name, string? path) => new()
    { Id = "Fixture::Method::" + name, Name = name, Type = CodeNodeType.Method, FilePath = path, ProjectContext = "Fixture", SourceSnippet = "secret-source" };

    private static StoredProjectProfile Stored(ProjectInventoryFile? target)
    {
        var profile = ToolCapabilityPolicyTests.Profile();
        return new(1, 1, DateTimeOffset.UtcNow, profile.Profile!, target) { AnalyzerOutcomes = profile.AnalyzerOutcomes };
    }
}
