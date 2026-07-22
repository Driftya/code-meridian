using CodeMeridian.Application.Services;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Knowledge;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace CodeMeridian.Application.Tests.Services;

public sealed class CodebaseQueryServiceProjectAnalysisOptionsTests
{
    [Fact]
    public async Task FindHotspotsAsync_ResolvesAnalysisPerProject()
    {
        var graph = Substitute.For<ICodeGraphRepository>();
        var vector = Substitute.For<IVectorRepository>();
        (CodeNode Node, int FanIn)[] hotspotResults =
        [
            (Node("prod", "BillingService", CodeNodeType.Class, "src/BillingService.cs", IndexedFileRole.Source), 10),
            (Node("heur", "Orders", CodeNodeType.Namespace, "src/Orders", IndexedFileRole.Source), 9)
        ];

        graph.FindHotspotsAsync("ProjectA", 15, Arg.Any<CancellationToken>())
            .Returns(hotspotResults);
        graph.FindHotspotsAsync("ProjectB", 15, Arg.Any<CancellationToken>())
            .Returns(hotspotResults);

        var sut = new CodebaseQueryService(
            graph,
            vector,
            new NoOpEmbeddingProvider(),
            Options.Create(new CodebaseAnalysisOptions()),
            Options.Create(new CodebaseIndexingOptions()),
            new DefaultAnalysisProfilePolicy(),
            new FakeProjectAnalysisOptionsResolver(project =>
            {
                var options = new CodebaseAnalysisOptions();
                if (string.Equals(project, "ProjectA", StringComparison.Ordinal))
                    options.Ranking.ProductionOnlyByDefault = false;

                return new ResolvedProjectAnalysisOptions(
                    options,
                    new AnalysisOptionsResolutionMetadata(false, true, []));
            }));

        var broader = await sut.FindHotspotsAsync("ProjectA");
        var narrow = await sut.FindHotspotsAsync("ProjectB");

        broader.Should().Contain("### Broader heuristic matches");
        narrow.Should().NotContain("### Broader heuristic matches");
    }

    [Fact]
    public async Task FindHotspotsAsync_ConcurrentProjects_DoNotShareAnalysisScope()
    {
        var graph = Substitute.For<ICodeGraphRepository>();
        var vector = Substitute.For<IVectorRepository>();
        var projectAEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var projectBEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseProjectA = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseProjectB = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        (CodeNode Node, int FanIn)[] hotspotResults =
        [
            (Node("prod", "BillingService", CodeNodeType.Class, "src/BillingService.cs", IndexedFileRole.Source), 10),
            (Node("heur", "Orders", CodeNodeType.Namespace, "src/Orders", IndexedFileRole.Source), 9)
        ];

        graph.FindHotspotsAsync("ProjectA", 15, Arg.Any<CancellationToken>())
            .Returns(releaseProjectA.Task.ContinueWith<IReadOnlyList<(CodeNode Node, int FanIn)>>(
                _ => hotspotResults,
                TaskScheduler.Default))
            .AndDoes(_ => projectAEntered.TrySetResult(true));
        graph.FindHotspotsAsync("ProjectB", 15, Arg.Any<CancellationToken>())
            .Returns(releaseProjectB.Task.ContinueWith<IReadOnlyList<(CodeNode Node, int FanIn)>>(
                _ => hotspotResults,
                TaskScheduler.Default))
            .AndDoes(_ => projectBEntered.TrySetResult(true));

        var sut = new CodebaseQueryService(
            graph,
            vector,
            new NoOpEmbeddingProvider(),
            Options.Create(new CodebaseAnalysisOptions()),
            Options.Create(new CodebaseIndexingOptions()),
            new DefaultAnalysisProfilePolicy(),
            new FakeProjectAnalysisOptionsResolver(project =>
            {
                var options = new CodebaseAnalysisOptions();
                options.Ranking.ProductionOnlyByDefault = !string.Equals(project, "ProjectA", StringComparison.Ordinal);
                return new ResolvedProjectAnalysisOptions(
                    options,
                    new AnalysisOptionsResolutionMetadata(false, true, []));
            }));

        var projectATask = sut.FindHotspotsAsync("ProjectA");
        var projectBTask = sut.FindHotspotsAsync("ProjectB");
        await Task.WhenAll(projectAEntered.Task, projectBEntered.Task);

        releaseProjectA.TrySetResult(true);
        var projectAResult = await projectATask;
        releaseProjectB.TrySetResult(true);
        var projectBResult = await projectBTask;

        projectAResult.Should().Contain("### Broader heuristic matches");
        projectBResult.Should().NotContain("### Broader heuristic matches");
    }

    private static CodeNode Node(string id, string name, CodeNodeType type, string filePath, IndexedFileRole fileRole) => new()
    {
        Id = id,
        Name = name,
        Type = type,
        FilePath = filePath,
        FileRole = fileRole
    };

    private sealed class FakeProjectAnalysisOptionsResolver(Func<string?, ResolvedProjectAnalysisOptions> factory) : IProjectAnalysisOptionsResolver
    {
        public ValueTask<ResolvedProjectAnalysisOptions> ResolveAsync(string? projectContext, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(factory(projectContext));
    }
}
