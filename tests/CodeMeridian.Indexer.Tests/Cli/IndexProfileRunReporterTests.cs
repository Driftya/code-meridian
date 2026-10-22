using System.Net;
using System.Net.Http.Json;
using CodeMeridian.Core.Projects;
using CodeMeridian.Indexer.Cli.Commands;
using CodeMeridian.Tooling.Discovery;
using CodeMeridian.Tooling.Storage;
using FluentAssertions;

namespace CodeMeridian.Indexer.Tests.Cli;

public sealed class IndexProfileRunReporterTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"index-outcomes-{Guid.NewGuid():N}"));

    [Theory]
    [InlineData(0, true, "succeeded", "complete")]
    [InlineData(0, false, "succeeded", "unknown")]
    [InlineData(2, true, "partial", "partial")]
    [InlineData(1, true, "failed", "unknown")]
    public async Task PublicationPrecedesAnalysisAndRecordsActualCoverage(int exit, bool full, string state, string coverage)
    {
        File.WriteAllText(Path.Combine(_root.FullName, "chapter.md"), "# Chapter");
        var paths = new List<string>();
        var outcomes = new List<ProjectAnalyzerOutcomeSnapshot>();
        var reporter = Reporter(paths, outcomes);
        var result = await reporter.RunAsync(Settings(), async report =>
        {
            paths.Should().Equal("/api/v1/project-profiles/begin", "/api/v1/project-profiles/");
            report!.CoversAllDocuments([]).Should().BeFalse();
            report.CoversAllDocuments(["chapter.md"]).Should().BeTrue();
            return await report.RunAnalyzerAsync("documents", ["."], async () =>
            {
                await report.ReadSourceAsync(new(Path.Combine(_root.FullName, "chapter.md")), CancellationToken.None);
                return exit;
            }, full, verifiedCoverage: true);
        });
        result.Should().Be(exit);
        outcomes.Should().HaveCount(2);
        outcomes.Should().OnlyContain(o => o.Generation == 3 && o.Project == "Docs");
        outcomes[0].Outcomes.Single().State.Should().Be("running");
        outcomes[1].Outcomes.Single().State.Should().Be(state);
        outcomes[1].Outcomes.Single().Coverage.Should().Be(coverage);
        outcomes.Should().OnlyContain(snapshot => snapshot.Outcomes.All(outcome =>
            outcome.Fingerprint == ProjectAnalysisFingerprints.Analyzer("documents")));
    }

    [Fact]
    public async Task ProcessSuccessWithoutAuditedCoverageRemainsUnknown()
    {
        var outcomes = new List<ProjectAnalyzerOutcomeSnapshot>();
        var reporter = Reporter([], outcomes);
        (await reporter.RunAsync(Settings(), report => report!.RunAnalyzerAsync("csharp", ["."], () => Task.FromResult(0), true)))
            .Should().Be(0);
        outcomes.Last().Outcomes.Single().Coverage.Should().Be("unknown");
    }

    [Theory]
    [InlineData("changed", false)]
    [InlineData("deleted", false)]
    [InlineData("added", false)]
    [InlineData("changed", true)]
    [InlineData("deleted", true)]
    [InlineData("added", true)]
    public async Task SourceChangesDuringOrAfterAnAnalyzerPreventCompletePublication(string change, bool afterAnalyzer)
    {
        var path = Path.Combine(_root.FullName, "chapter.md");
        File.WriteAllText(path, "# Before");
        var outcomes = new List<ProjectAnalyzerOutcomeSnapshot>();
        var result = await Reporter([], outcomes).RunAsync(Settings(), async report =>
        {
            var exit = await report!.RunAnalyzerAsync("documents", ["."], async () =>
            {
                await report.ReadSourceAsync(new(path), CancellationToken.None);
                if (!afterAnalyzer) Mutate();
                return 0;
            }, true, verifiedCoverage: true);
            outcomes.SelectMany(item => item.Outcomes).Should().OnlyContain(item => item.State == "running");
            if (afterAnalyzer) Mutate();
            return exit;
        });
        result.Should().Be(2);
        outcomes.Last().Outcomes.Single().State.Should().Be("failed");
        outcomes.SelectMany(item => item.Outcomes).Should().NotContain(item => item.Coverage == "complete");

        void Mutate()
        {
            if (change == "changed") File.WriteAllText(path, "# Changed");
            else if (change == "deleted") File.Delete(path);
            else File.WriteAllText(Path.Combine(_root.FullName, "added.md"), "# Added");
        }
    }

    [Fact]
    public async Task SourceChangesBeforeAnalysisPreventTheCallbackFromRunning()
    {
        var path = Path.Combine(_root.FullName, "chapter.md");
        File.WriteAllText(path, "# Before");
        var invoked = false;
        var outcomes = new List<ProjectAnalyzerOutcomeSnapshot>();
        var result = await Reporter([], outcomes).RunAsync(Settings(), report =>
        {
            File.WriteAllText(path, "# Changed");
            return report!.RunAnalyzerAsync("documents", ["."], () => { invoked = true; return Task.FromResult(0); }, true, verifiedCoverage: true);
        });
        result.Should().Be(2);
        invoked.Should().BeFalse();
        outcomes.Last().Outcomes.Single().Coverage.Should().Be("unknown");
    }

    [Fact]
    public async Task UnreadDocumentsAndUnauditedWorkersCannotClaimCompleteCoverage()
    {
        File.WriteAllText(Path.Combine(_root.FullName, "chapter.md"), "# Before");
        var outcomes = new List<ProjectAnalyzerOutcomeSnapshot>();
        await Reporter([], outcomes).RunAsync(Settings(), async report =>
        {
            (await report!.RunAnalyzerAsync("documents", ["."], () => Task.FromResult(0), true, verifiedCoverage: true))
                .Should().Be(2);
            return await report.RunAnalyzerAsync("csharp", ["."], () => Task.FromResult(0), true, verifiedCoverage: true);
        });
        outcomes.SelectMany(item => item.Outcomes).Should().NotContain(item => item.Coverage == "complete");
        outcomes.Last().Outcomes.Single(item => item.Analyzer == "documents").State.Should().Be("partial");
    }

    [Fact]
    public async Task UnchangedReadBytesBindTheTerminalOutcomeToThePublishedSnapshot()
    {
        var path = Path.Combine(_root.FullName, "chapter.md");
        File.WriteAllText(path, "# Before");
        var outcomes = new List<ProjectAnalyzerOutcomeSnapshot>();
        ProjectProfileSnapshot? snapshot = null;
        var reporter = Reporter([], outcomes, value => snapshot = value);
        var result = await reporter.RunAsync(Settings(), report => report!.RunAnalyzerAsync("documents", ["."], async () =>
        {
            (await report.ReadSourceAsync(new(path), CancellationToken.None)).Should().Be("# Before");
            return 0;
        }, true, verifiedCoverage: true));
        result.Should().Be(0);
        snapshot!.Profile.SourceFingerprint.Should().StartWith("sha256:").And.HaveLength(71);
        outcomes.SelectMany(item => item.Outcomes).Should().OnlyContain(item => item.SourceFingerprint == snapshot.Profile.SourceFingerprint);
        outcomes.Last().Outcomes.Single().Coverage.Should().Be("complete");
    }

    [Fact]
    public async Task ReadOfTransientDifferentBytesCannotBeHiddenByRestoringTheFile()
    {
        var path = Path.Combine(_root.FullName, "chapter.md");
        File.WriteAllText(path, "# Before");
        var outcomes = new List<ProjectAnalyzerOutcomeSnapshot>();
        var result = await Reporter([], outcomes).RunAsync(Settings(), report => report!.RunAnalyzerAsync("documents", ["."], async () =>
        {
            File.WriteAllText(path, "# Changed");
            var read = () => report.ReadSourceAsync(new(path), CancellationToken.None);
            await read.Should().ThrowAsync<IOException>();
            File.WriteAllText(path, "# Before");
            return 0;
        }, true, verifiedCoverage: true));
        result.Should().Be(2);
        outcomes.Last().Outcomes.Single().State.Should().Be("failed");
    }

    [Fact]
    public async Task SourceChangeWhileUploadingTerminalEvidenceReservesANewGenerationToInvalidateIt()
    {
        var path = Path.Combine(_root.FullName, "chapter.md");
        File.WriteAllText(path, "# Before");
        var outcomes = new List<ProjectAnalyzerOutcomeSnapshot>();
        var paths = new List<string>();
        var reporter = Reporter(paths, outcomes, terminalPublished: () => File.WriteAllText(path, "# Changed"));
        var result = await reporter.RunAsync(Settings(), report => report!.RunAnalyzerAsync("documents", ["."], async () =>
        {
            await report.ReadSourceAsync(new(path), CancellationToken.None);
            return 0;
        }, true, verifiedCoverage: true));
        result.Should().Be(2);
        paths.Count(item => item == "/api/v1/project-profiles/begin").Should().Be(2);
        paths.Last().Should().Be("/api/v1/project-profiles/begin");
    }

    [Fact]
    public async Task FailedPublicationNeverStartsTheAnalyzer()
    {
        var reporter = new IndexProfileRunReporter(new(new RepositoryInventoryDiscovery()))
        {
            HttpClientFactory = (_, _) => new HttpClient(new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))))
                { BaseAddress = new Uri("http://localhost") }
        };
        var invoked = false;
        (await reporter.RunAsync(Settings(), _ => { invoked = true; return Task.FromResult(0); })).Should().Be(1);
        invoked.Should().BeFalse();
    }

    private IndexProfileRunReporter Reporter(List<string> paths, List<ProjectAnalyzerOutcomeSnapshot> outcomes,
        Action<ProjectProfileSnapshot>? published = null, Action? terminalPublished = null) => new(new(new RepositoryInventoryDiscovery()))
    {
        WorkerFingerprintReader = (_, _) => Task.FromResult<ProjectAnalyzerFingerprint?>(null),
        HttpClientFactory = (_, _) => new HttpClient(new Handler(async request =>
        {
            paths.Add(request.RequestUri!.AbsolutePath);
            if (paths.Last().EndsWith("/outcomes"))
            {
                outcomes.Add((await request.Content!.ReadFromJsonAsync<ProjectAnalyzerOutcomeSnapshot>())!);
                if (outcomes.Last().Outcomes.Any(item => item.State != "running")) terminalPublished?.Invoke();
            }
            if (request.Method == HttpMethod.Post && paths.Last() == "/api/v1/project-profiles/")
                published?.Invoke((await request.Content!.ReadFromJsonAsync<ProjectProfileSnapshot>())!);
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { generation = 3 }) };
        })) { BaseAddress = new Uri("http://localhost") }
    };

    private ResolvedIndexerSettings Settings() => new() { RootPath = _root, Project = "Docs", CodeMeridianUrl = "http://localhost",
        StorageMode = IndexerStorageMode.Repository, IncludeDocs = true };
    public void Dispose() => _root.Delete(recursive: true);

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request);
    }
}
