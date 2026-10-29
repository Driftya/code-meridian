using System.Net;
using System.Net.Http.Json;
using CodeMeridian.Core.Projects;
using CodeMeridian.Indexer.Cli.Commands;
using CodeMeridian.Tooling.Discovery;
using CodeMeridian.Tooling.Storage;
using FluentAssertions;

namespace CodeMeridian.Indexer.Tests.Cli;

public sealed class ProducerEvidencePublicationTests : IDisposable
{
    private readonly DirectoryInfo root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "producer-evidence-" + Guid.NewGuid().ToString("N")));
    private readonly List<ProjectAnalyzerOutcomeSnapshot> outcomes = [];
    private static readonly ProjectAnalyzerFingerprint Worker = new("sha256:" + new string('a', 64), "sha256:" + new string('b', 64));

    [Theory]
    [InlineData("complete", 0, "complete")]
    [InlineData("missing_report", 0, "unknown")]
    [InlineData("unknown_capability", 0, "unknown")]
    [InlineData("partial", 2, "partial")]
    [InlineData("missing_file", 2, "partial")]
    [InlineData("unpublished", 2, "partial")]
    [InlineData("delta", 0, "unknown")]
    [InlineData("hash", 1, "unknown")]
    [InlineData("worker", 1, "unknown")]
    [InlineData("project", 1, "unknown")]
    [InlineData("scope", 1, "unknown")]
    [InlineData("duplicate", 1, "unknown")]
    public async Task OnlyCompleteBoundPublishedInventoryCanEstablishCoverage(string mode, int exit, string coverage)
    {
        File.WriteAllText(Path.Combine(root.FullName, "a.sql"), "SELECT 1;");
        File.WriteAllText(Path.Combine(root.FullName, "b.sql"), "SELECT 2;");
        var reporter = new IndexProfileRunReporter(new(new RepositoryInventoryDiscovery()))
        {
            WorkerFingerprintReader = (_, _) => Task.FromResult<ProjectAnalyzerFingerprint?>(Worker),
            HttpClientFactory = (_, _) => new HttpClient(new Respond(async request =>
            {
                if (request.RequestUri!.AbsolutePath.EndsWith("/outcomes"))
                    outcomes.Add((await request.Content!.ReadFromJsonAsync<ProjectAnalyzerOutcomeSnapshot>())!);
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { generation = 1 }) };
            })) { BaseAddress = new("http://fixture") }
        };
        var settings = new ResolvedIndexerSettings { RootPath = root, Project = "Fixture", CodeMeridianUrl = "http://fixture",
            StorageMode = IndexerStorageMode.Repository, Sql = new() { Enabled = true }, IncludeDocs = false,
            SkipCSharp = true, SkipTypeScript = true, SkipConfiguration = true };

        var result = await reporter.RunAsync(settings, report => report!.RunAnalyzerAsync("sql", ["."], () =>
        {
            if (mode == "missing_report") return Task.FromResult(0);
            var files = report.ProducerPaths("sql").Select(path => new ProducerFileOutcome(path, report.SourceFileFingerprint(path), "complete", [], "postgresql", "db")).ToArray();
            if (mode == "missing_file") files = [files[0]];
            if (mode == "partial") files[0] = files[0] with { Status = "partial" };
            if (mode == "hash") files[0] = files[0] with { SourceFingerprint = "sha256:" + new string('c', 64) };
            if (mode == "duplicate") files = [.. files, files[0]];
            report.AcceptProducerReport(new("1", "fixture", "sql", mode == "project" ? "Other" : "Fixture", mode == "scope" ? "elsewhere" : ".",
                mode == "worker" ? new(Worker.Parser, Worker.Implementation) : Worker, mode != "unpublished", files,
                new Dictionary<string, string> { ["sql_object_dependencies"] = mode == "unknown_capability" ? "unknown" : "complete" }, [], report.Generation, report.SourceFingerprint));
            return Task.FromResult(0);
        }, fullSelection: mode != "delta"));

        result.Should().Be(exit);
        outcomes.Last().Outcomes.Single().Coverage.Should().Be(coverage);
        if (exit == 1) outcomes.Last().Outcomes.Single().State.Should().Be("failed");
    }

    public void Dispose() => root.Delete(true);
    private sealed class Respond(Func<HttpRequestMessage, Task<HttpResponseMessage>> response) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => response(request); }
}
