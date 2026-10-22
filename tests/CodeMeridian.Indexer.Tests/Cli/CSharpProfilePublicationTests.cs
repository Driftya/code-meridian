using System.Net;
using System.Net.Http.Json;
using CodeMeridian.Core.Projects;
using CodeMeridian.Indexer.Cli.Commands;
using CodeMeridian.RoslynIndexer.Pipeline;
using CodeMeridian.Sdk;
using CodeMeridian.Tooling.Discovery;
using CodeMeridian.Tooling.Storage;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace CodeMeridian.Indexer.Tests.Cli;

public sealed class CSharpProfilePublicationTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"csharp-publication-{Guid.NewGuid():N}"));
    private readonly List<ProjectAnalyzerOutcomeSnapshot> _outcomes = [];
    private ProjectProfileSnapshot? _snapshot;

    [Theory]
    [InlineData("class Main {}", true, 0, "succeeded", "unknown")]
    [InlineData("", true, 0, "succeeded", "unknown")]
    [InlineData("class Main {}", false, 0, "succeeded", "unknown")]
    [InlineData("class Main { Missing field; void Run() { DoesNotExist(); } }", true, 0, "partial", "partial")]
    [InlineData("class Main { void Run(dynamic receiver) { receiver.Execute(); } }", true, 0, "partial", "partial")]
    [InlineData("class Main { void Run() { System.Console.WriteLine(); } }", true, 0, "succeeded", "unknown")]
    [InlineData("class Main { ~Main() {} }", true, 0, "partial", "partial")]
    [InlineData("class Main { void Run() {} void Run<T>() {} }", true, 0, "partial", "partial")]
    [InlineData("class Main { int Value { get; set; } }", true, 0, "partial", "partial")]
    [InlineData("class Main { void Run() { System.Action action = () => {}; } }", true, 0, "partial", "partial")]
    [InlineData("class Main(int value) {}", true, 0, "partial", "partial")]
    [InlineData("class {", true, 2, "partial", "partial")]
    [InlineData("using Alias = System.String; using Alias = System.Int32; class Main {}", true, 2, "partial", "partial")]
    public async Task TerminalOutcomesUseWorkerEvidence(string source, bool full, int exitCode, string state, string coverage)
    {
        Write("Main.cs", source);

        var result = await Reporter().RunAsync(Settings(), report =>
            report!.RunCSharpAnalyzerAsync(() => RunWorker(report, full), full));

        result.Should().Be(exitCode);
        _outcomes.Should().HaveCount(2);
        _outcomes[0].Outcomes.Single().State.Should().Be("running");
        var terminal = _outcomes[1].Outcomes.Single();
        terminal.State.Should().Be(state);
        terminal.Coverage.Should().Be(coverage);
        terminal.Mode.Should().Be(full ? "full" : "delta");
        terminal.Limitations.Should().Contain(limit => limit.StartsWith("C# worker:"));
        terminal.Limitations.Should().Contain(limit => limit.Contains("unaudited"));
        terminal.Limitations.Should().Contain(limit => limit.Contains("semantic errors"));
        terminal.Limitations.Should().Contain(limit => limit.StartsWith("Observed relationships:"));
        terminal.Limitations.Should().Contain(limit => limit.StartsWith("Callable syntax:"));
        terminal.SourceFingerprint.Should().Be(_snapshot!.Profile.SourceFingerprint);
        terminal.Fingerprint.Should().Be(_snapshot.Profile.Analyzers.Single(analyzer => analyzer.Id == "csharp").Fingerprint);
    }

    [Fact]
    public async Task SkippedGeneratedInputsPublishPartialEvidence()
    {
        Write("Main.cs", "class Main {}");
        Write("Generated.g.cs", "class Generated {}");

        var result = await Reporter().RunAsync(Settings(), report =>
            report!.RunCSharpAnalyzerAsync(() => RunWorker(report), true));

        result.Should().Be(0);
        var terminal = _outcomes.Last().Outcomes.Single();
        terminal.State.Should().Be("partial");
        terminal.Coverage.Should().Be("partial");
        terminal.Limitations.Should().Contain(limit => limit.Contains("1 skipped"));
    }

    [Fact]
    public async Task MissingWorkerReportCannotPublishSuccess()
    {
        var result = await Reporter().RunAsync(Settings(), report =>
            report!.RunCSharpAnalyzerAsync(() => Task.FromResult(new IndexStats(0, 0, 0, 0, 0, 0, 0, 0,
                new Dictionary<string, int>(), null!, null!, "full", true,
                new Dictionary<string, int>(), new Dictionary<string, int>())), true));

        result.Should().Be(2);
        _outcomes.Last().Outcomes.Single().State.Should().Be("partial");
        _outcomes.SelectMany(snapshot => snapshot.Outcomes).Should().NotContain(outcome => outcome.Coverage == "complete");
    }

    [Fact]
    public async Task TransientDifferentBytesReadByTheWorkerInvalidatePublicationEvenAfterRestoration()
    {
        var file = Write("Main.cs", "class Main {}");
        var result = await Reporter().RunAsync(Settings(), report => report!.RunCSharpAnalyzerAsync(() =>
            RunWorker(report, readSource: async (source, token) =>
            {
                File.WriteAllText(file.FullName, "class Changed {}");
                try { return await report.ReadSourceAsync(source, token); }
                finally { File.WriteAllText(file.FullName, "class Main {}"); }
            }), true));

        result.Should().Be(2);
        _outcomes.Last().Outcomes.Single().State.Should().Be("failed");
    }

    [Fact]
    public async Task FailedGraphUploadPublishesFailedOutcome()
    {
        Write("Main.cs", "class Main {}");
        var result = await Reporter().RunAsync(Settings(), report =>
            report!.RunCSharpAnalyzerAsync(() => RunWorker(report, failUpload: true), true));

        result.Should().Be(1);
        _outcomes.Last().Outcomes.Single().State.Should().Be("failed");
    }

    private Task<IndexStats> RunWorker(IndexProfileRun report, bool full = true, bool failUpload = false,
        Func<FileInfo, CancellationToken, Task<string>>? readSource = null)
    {
        var client = new CodeMeridianClient(new HttpClient(new Respond(_ => Task.FromResult(
            new HttpResponseMessage(failUpload ? HttpStatusCode.BadRequest : HttpStatusCode.Created)
            { Content = JsonContent.Create(new { }) }))) { BaseAddress = new Uri("http://localhost") });
        var pipeline = new IndexerPipeline(new(client, NullLogger<CSharpIndexer>.Instance), client, NullLogger<IndexerPipeline>.Instance);
        return pipeline.RunAsync(_root, "Example", false, changedFiles: full ? null : ["Main.cs"],
            readSource: readSource ?? report.ReadSourceAsync);
    }

    private IndexProfileRunReporter Reporter() => new(new(new RepositoryInventoryDiscovery()))
    {
        HttpClientFactory = (_, _) => new HttpClient(new Respond(async request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/outcomes"))
                _outcomes.Add((await request.Content!.ReadFromJsonAsync<ProjectAnalyzerOutcomeSnapshot>())!);
            else if (request.RequestUri.AbsolutePath == "/api/v1/project-profiles/")
                _snapshot = await request.Content!.ReadFromJsonAsync<ProjectProfileSnapshot>();
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { generation = 3 }) };
        })) { BaseAddress = new Uri("http://localhost") }
    };

    private ResolvedIndexerSettings Settings() => new()
    {
        RootPath = _root, Project = "Example", CodeMeridianUrl = "http://localhost",
        StorageMode = IndexerStorageMode.Repository, SkipTypeScript = true, SkipConfiguration = true
    };

    private FileInfo Write(string name, string content)
    {
        var file = new FileInfo(Path.Combine(_root.FullName, name));
        File.WriteAllText(file.FullName, content);
        return file;
    }
    public void Dispose() => _root.Delete(recursive: true);

    private sealed class Respond(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request);
    }
}
