using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CodeMeridian.Core.Projects;
using CodeMeridian.Indexer.Cli.Commands;
using CodeMeridian.Tooling.Discovery;
using CodeMeridian.Tooling.Storage;
using FluentAssertions;

namespace CodeMeridian.Indexer.Tests.Cli;

public sealed class NodeWorkerIdentityTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"worker-identity-{Guid.NewGuid():N}"));
    private static readonly ProjectAnalyzerFingerprint Fingerprint = new("sha256:" + new string('a', 64), "sha256:" + new string('b', 64));

    [Theory]
    [InlineData("typescript", true)]
    [InlineData("frontend", true)]
    [InlineData("sql", true)]
    [InlineData("typescript", false)]
    [InlineData("frontend", false)]
    [InlineData("sql", false)]
    public async Task ReportedIdentityFlowsThroughProfileAndTerminalOutcomes(string analyzer, bool available)
    {
        var settings = Settings(analyzer);
        ProjectProfileSnapshot? published = null;
        var outcomes = new List<ProjectAnalyzerOutcomeSnapshot>();
        var expected = available ? Fingerprint : null;
        var reporter = Reporter(analyzer, expected, async request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/outcomes"))
                outcomes.Add((await request.Content!.ReadFromJsonAsync<ProjectAnalyzerOutcomeSnapshot>())!);
            else if (request.RequestUri.AbsolutePath.EndsWith('/'))
                published = await request.Content!.ReadFromJsonAsync<ProjectProfileSnapshot>();
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { generation = 3 }) };
        });

        (await reporter.RunAsync(settings, run => run!.RunAnalyzerAsync(analyzer, ["."], () =>
        {
            published!.Profile.Analyzers.Single(item => item.Id == analyzer).Fingerprint.Should().Be(expected);
            run.Fingerprint(analyzer).Should().Be(expected);
            return Task.FromResult(0);
        }, fullSelection: true))).Should().Be(0);

        outcomes.SelectMany(report => report.Outcomes).Should().HaveCount(2)
            .And.OnlyContain(outcome => outcome.Fingerprint == expected && outcome.Coverage == "unknown"
                && outcome.SourceFingerprint == published!.Profile.SourceFingerprint);
    }

    [Theory]
    [InlineData("typescript", "same", true)]
    [InlineData("typescript", "implementation", false)]
    [InlineData("typescript", "parser", false)]
    [InlineData("typescript", "missing-current", false)]
    [InlineData("typescript", "missing-published", false)]
    [InlineData("typescript", "both-missing", false)]
    [InlineData("frontend", "same", true)]
    [InlineData("frontend", "parser", false)]
    [InlineData("frontend", "both-missing", false)]
    [InlineData("sql", "same", true)]
    [InlineData("sql", "parser", false)]
    [InlineData("sql", "both-missing", false)]
    public async Task CacheReuseRequiresKnownMatchingWorkerAndParserIdentity(string analyzer, string change, bool expected)
    {
        var settings = Settings(analyzer);
        var snapshot = new LocalProjectProfileBuilder(new RepositoryInventoryDiscovery()).BuildSnapshot(settings, 3);
        var cache = IncrementalIndexCache.Load(new DirectoryInfo(Path.Combine(_root.FullName, "cache")), "Fixture");
        cache.Save(cache.BuildPlan(_root, [], forceFull: true), settings.CodeMeridianUrl, 3, snapshot.Profile.ObservedAt);
        var current = change switch
        {
            "implementation" => Fingerprint with { Implementation = "sha256:" + new string('c', 64) },
            "parser" => Fingerprint with { Parser = "sha256:" + new string('c', 64) },
            "missing-current" or "both-missing" => null,
            _ => Fingerprint
        };
        var profile = snapshot.Profile with
        {
            SourceFingerprint = "sha256:" + new string('a', 64),
            Analyzers = snapshot.Profile.Analyzers.Select(item => NodeWorkerIdentity.IsWorker(item.Id)
                ? item with { Fingerprint = change is "missing-published" or "both-missing" ? null : Fingerprint } : item).ToArray()
        };
        var reporter = Reporter(analyzer, current, _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new
        {
            status = "available", project = "Fixture", generation = 3, latestGeneration = 3, profile, contractVersion = "1.0"
        }) }));

        (await reporter.IsCacheCurrentAsync(settings, cache)).Should().Be(expected);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("not JSON")]
    [InlineData("{\"contractVersion\":\"2\",\"analyzer\":\"typescript\"}")]
    [InlineData("{\"contractVersion\":\"1\",\"analyzer\":\"sql\"}")]
    [InlineData("{\"contractVersion\":\"1\",\"analyzer\":\"typescript\",\"fingerprint\":{\"implementation\":\"v1\",\"parser\":\"v1\"}}")]
    public void MissingMalformedOrMismatchedReportsRemainUnknown(string output) =>
        NodeWorkerIdentity.Parse("typescript", output).Should().BeNull();

    [Fact]
    public void ValidBoundedWorkerReportCanBeRead()
    {
        var output = JsonSerializer.Serialize(new { contractVersion = "1", analyzer = "typescript", fingerprint = Fingerprint },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        NodeWorkerIdentity.Parse("typescript", output).Should().Be(Fingerprint);
        NodeWorkerIdentity.Parse("typescript", new string(' ', 8193) + output).Should().BeNull();
    }

    [Fact]
    public async Task IdentityDiscoveryPropagatesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var action = () => NodeWorkerIdentity.ReadAsync("typescript", cancellation.Token);
        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    private static IndexProfileRunReporter Reporter(string analyzer, ProjectAnalyzerFingerprint? fingerprint,
        Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) => new(new(new RepositoryInventoryDiscovery()))
    {
        WorkerFingerprintReader = (id, _) => Task.FromResult(id == analyzer ? fingerprint : Fingerprint),
        HttpClientFactory = (_, _) => new HttpClient(new Handler(respond)) { BaseAddress = new Uri("http://localhost") }
    };

    private ResolvedIndexerSettings Settings(string analyzer) => new()
    {
        RootPath = _root, Project = "Fixture", CodeMeridianUrl = "http://localhost", StorageMode = IndexerStorageMode.Repository,
        SkipCSharp = true, IncludeDocs = false, SkipTypeScript = analyzer == "sql", SkipConfiguration = true,
        Sql = new CodeMeridian.Tooling.Configuration.CodeMeridianSqlOptions { Enabled = analyzer == "sql" }
    };

    public void Dispose() => _root.Delete(recursive: true);
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            respond(request);
    }
}
