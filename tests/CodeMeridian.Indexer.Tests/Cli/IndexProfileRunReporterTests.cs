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
            return await report.RunAnalyzerAsync("documents", ["."], () => Task.FromResult(exit), full, verifiedCoverage: true);
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

    private IndexProfileRunReporter Reporter(List<string> paths, List<ProjectAnalyzerOutcomeSnapshot> outcomes) => new(new(new RepositoryInventoryDiscovery()))
    {
        HttpClientFactory = (_, _) => new HttpClient(new Handler(async request =>
        {
            paths.Add(request.RequestUri!.AbsolutePath);
            if (paths.Last().EndsWith("/outcomes")) outcomes.Add((await request.Content!.ReadFromJsonAsync<ProjectAnalyzerOutcomeSnapshot>())!);
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
