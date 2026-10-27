using System.Net;
using System.Net.Http.Json;
using CodeMeridian.Core.Projects;
using CodeMeridian.Core.RepositoryConstraints;
using CodeMeridian.Indexer.Cli.Commands;
using CodeMeridian.Tooling.Discovery;
using CodeMeridian.Tooling.Storage;
using FluentAssertions;

namespace CodeMeridian.Indexer.Tests.Cli;

public sealed class MarkdownEvidencePublicationTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"markdown-publication-{Guid.NewGuid():N}"));

    [Fact]
    public async Task VerifiedDecodedBytesAndEmptyMarkdownFilesProduceBoundFileFactsWithoutSearchReadiness()
    {
        File.WriteAllText(Path.Combine(_root.FullName, "a.md"), "[target](b.md)", System.Text.Encoding.Unicode);
        File.WriteAllText(Path.Combine(_root.FullName, "b.md"), "");
        File.WriteAllText(Path.Combine(_root.FullName, "other.txt"), "[text](missing.md)");
        ProjectProfileSnapshot? snapshot = null;
        var result = await Reporter(value => snapshot = value).RunAsync(Settings(), report =>
            report!.RunAnalyzerAsync("documents", ["."], () => Task.FromResult(0), true, verifiedCoverage: true));
        result.Should().Be(2, "structural reads do not prove searchable document ingestion");
        snapshot!.Validate().Should().BeNull();
        var evidence = snapshot.MarkdownEvidence!;
        evidence.SourceFingerprint.Should().Be(snapshot.Profile.SourceFingerprint);
        evidence.Status.Should().Be("complete");
        evidence.Documents.Select(document => document.SourcePath).Should().Equal("a.md", "b.md");
        evidence.Documents[0].References.Should().ContainSingle().Which.NormalizedPath.Should().Be("b.md");
        evidence.Documents[1].References.Should().BeEmpty();
        evidence.Documents.Should().OnlyContain(document => document.Generation == snapshot.Generation && document.ParserFingerprint == evidence.ParserFingerprint);
    }

    [Fact]
    public async Task NewPassReplacesChangedRenamedAndDeletedSourcesWithoutCarryingOldFacts()
    {
        var path = Path.Combine(_root.FullName, "a.md");
        File.WriteAllText(path, "[old](old.md)");
        var snapshots = new List<ProjectProfileSnapshot>();
        var reporter = Reporter(snapshots.Add);
        (await reporter.RunAsync(Settings(), _ => Task.FromResult(0))).Should().Be(0);
        File.WriteAllText(path, "[new](new.md)");
        (await reporter.RunAsync(Settings(), _ => Task.FromResult(0))).Should().Be(0);
        snapshots[1].MarkdownEvidence!.Documents.Single().References.Single().NormalizedPath.Should().Be("new.md");
        snapshots[1].MarkdownEvidence!.Documents.Single().TextFingerprint.Should().NotBe(snapshots[0].MarkdownEvidence!.Documents.Single().TextFingerprint);
        File.Move(path, Path.Combine(_root.FullName, "renamed.md"));
        (await reporter.RunAsync(Settings(), _ => Task.FromResult(0))).Should().Be(0);
        snapshots[2].MarkdownEvidence!.Documents.Single().FileId.Should().Be("Docs::doc::renamed.md");
        File.Delete(Path.Combine(_root.FullName, "renamed.md"));
        (await reporter.RunAsync(Settings(), _ => Task.FromResult(0))).Should().Be(0);
        snapshots[3].MarkdownEvidence!.Status.Should().Be("complete");
        snapshots[3].MarkdownEvidence!.Documents.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnsupportedOrFailedParsingPublishesIncompleteStructuralEvidence(bool failed)
    {
        File.WriteAllText(Path.Combine(_root.FullName, "a.md"), failed ? new string('>', 1000) + " text" : "<img src='a.png'>");
        ProjectProfileSnapshot? snapshot = null;
        (await Reporter(value => snapshot = value).RunAsync(Settings(), _ => Task.FromResult(0))).Should().Be(0);
        snapshot!.Validate().Should().BeNull();
        snapshot.MarkdownEvidence!.Status.Should().Be("incomplete");
        snapshot.MarkdownEvidence.Documents.Single().Diagnostics.Should().NotBeEmpty();
        if (failed) snapshot.MarkdownEvidence.Documents.Single().References.Should().BeEmpty();
    }

    [Fact]
    public async Task CollectionLimitsPublishExplicitlyIncompleteEvidenceWithoutRetainingPartialFiles()
    {
        for (var index = 0; index < 11; index++)
            File.WriteAllText(Path.Combine(_root.FullName, $"{index:00}.md"), string.Concat(Enumerable.Repeat("[x](a.md)\n", 1000)));
        ProjectProfileSnapshot? snapshot = null;
        (await Reporter(value => snapshot = value).RunAsync(Settings(), _ => Task.FromResult(0))).Should().Be(0);
        snapshot!.Validate().Should().BeNull();
        snapshot.MarkdownEvidence!.Status.Should().Be("incomplete");
        snapshot.MarkdownEvidence.Documents.Should().HaveCount(10);
        snapshot.MarkdownEvidence.Documents.Sum(document => document.References.Count).Should().Be(MarkdownReferenceEvidence.MaximumReferences);
    }

    [Fact]
    public async Task SourceChangeDuringInitialPublicationInvalidatesGenerationBeforeAnalysis()
    {
        var path = Path.Combine(_root.FullName, "a.md");
        File.WriteAllText(path, "[before](b.md)");
        var began = 0;
        var invoked = false;
        var reporter = Reporter(_ => File.WriteAllText(path, "[after](c.md)"), () => began++);
        (await reporter.RunAsync(Settings(), _ => { invoked = true; return Task.FromResult(0); })).Should().Be(2);
        began.Should().Be(2);
        invoked.Should().BeFalse();
    }

    [Fact]
    public async Task LaterSourceChangesInvalidateStructuralEvidenceEvenWithoutAnalyzerOutcomes()
    {
        var path = Path.Combine(_root.FullName, "a.md");
        File.WriteAllText(path, "[before](b.md)");
        var began = 0;
        var reporter = Reporter(_ => { }, () => began++);
        (await reporter.RunAsync(Settings(), _ =>
        {
            File.WriteAllText(path, "[after](c.md)");
            return Task.FromResult(0);
        })).Should().Be(2);
        began.Should().Be(2);
    }

    [Fact]
    public async Task SkippedDocumentsPublishNoStructuralEvidence()
    {
        File.WriteAllText(Path.Combine(_root.FullName, "a.md"), "[target](b.md)");
        ProjectProfileSnapshot? snapshot = null;
        (await Reporter(value => snapshot = value).RunAsync(Settings(false), _ => Task.FromResult(0))).Should().Be(0);
        snapshot!.MarkdownEvidence.Should().BeNull();
    }

    [Fact]
    public async Task TransientDifferentBytesAndCancellationCannotProduceStructuralFacts()
    {
        var path = Path.Combine(_root.FullName, "a.md");
        File.WriteAllText(path, "[before](b.md)");
        var snapshot = new LocalProjectProfileBuilder(new RepositoryInventoryDiscovery()).BuildSnapshot(Settings(), 3);
        var source = AnalyzedSourceSnapshot.Capture(_root, snapshot.Files, CancellationToken.None);
        File.WriteAllText(path, "[after](c.md)");
        await FluentActions.Awaiting(() => MarkdownEvidenceBuilder.BuildAsync(snapshot, _root, source, CancellationToken.None))
            .Should().ThrowAsync<IOException>();
        File.WriteAllText(path, "[before](b.md)");
        source.IsCurrent(CancellationToken.None).Should().BeFalse();
        source = AnalyzedSourceSnapshot.Capture(_root, snapshot.Files, CancellationToken.None);
        await FluentActions.Awaiting(() => MarkdownEvidenceBuilder.BuildAsync(snapshot, _root, source, new(true)))
            .Should().ThrowAsync<OperationCanceledException>();
    }

    private IndexProfileRunReporter Reporter(Action<ProjectProfileSnapshot> published, Action? began = null) => new(new(new RepositoryInventoryDiscovery()))
    {
        WorkerFingerprintReader = (_, _) => Task.FromResult<ProjectAnalyzerFingerprint?>(null),
        HttpClientFactory = (_, _) => new HttpClient(new Handler(async request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/begin", StringComparison.Ordinal)) began?.Invoke();
            if (request.RequestUri.AbsolutePath == "/api/v1/project-profiles/")
                published((await request.Content!.ReadFromJsonAsync<ProjectProfileSnapshot>())!);
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { generation = 3 }) };
        })) { BaseAddress = new Uri("http://localhost") }
    };

    private ResolvedIndexerSettings Settings(bool includeDocs = true) => new() { RootPath = _root, Project = "Docs", CodeMeridianUrl = "http://localhost",
        StorageMode = IndexerStorageMode.Repository, IncludeDocs = includeDocs };

    public void Dispose() => _root.Delete(recursive: true);

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request);
    }
}
