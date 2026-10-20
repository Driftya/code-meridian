using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using CodeMeridian.Core.Projects;
using CodeMeridian.Indexer.Cli.Commands;
using CodeMeridian.Tooling.Discovery;
using CodeMeridian.Tooling.Storage;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace CodeMeridian.Indexer.Tests.Cli;

public sealed class IndexProfileReconciliationTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"profile-reconcile-{Guid.NewGuid():N}"));
    private readonly DateTimeOffset _observedAt = DateTimeOffset.UtcNow;
    private readonly List<string> _requests = [];
    private readonly List<ProjectAnalyzerOutcomeSnapshot> _outcomes = [];
    private readonly List<ProjectProfileSnapshot> _snapshots = [];

    [Theory]
    [InlineData("available", 3L, 3L, 0, true)]
    [InlineData("unknown", null, null, 0, false)]
    [InlineData("stale", 3L, 4L, 0, false)]
    [InlineData("available", 4L, 4L, 0, false)]
    [InlineData("available", 3L, 3L, 1, false)]
    public async Task CurrentCacheRequiresMatchingServerGenerationAndObservation(
        string status, long? generation, long? latest, int observationOffset, bool expected)
    {
        var settings = Settings();
        var cache = SeedCache();
        var reporter = Reporter(status, generation, latest, _observedAt.AddSeconds(observationOffset));

        (await reporter.IsCacheCurrentAsync(settings, cache)).Should().Be(expected);
        _requests.Should().ContainSingle().Which.Should().StartWith("GET /api/v1/project-profiles/");
    }

    [Fact]
    public async Task LegacyCacheCannotEstablishPublication()
    {
        var cache = SeedCache(published: false);

        (await Reporter().IsCacheCurrentAsync(Settings(), cache)).Should().BeFalse();
        _requests.Should().BeEmpty();
        cache.BuildPlan(_root, [], forceFull: false).HasChanges.Should().BeFalse();
    }

    [Fact]
    public async Task CacheFromAnotherServerCannotEstablishPublication()
    {
        var cache = SeedCache(serverUrl: "http://another-server:5100");

        (await Reporter().IsCacheCurrentAsync(Settings(), cache)).Should().BeFalse();
        _requests.Should().BeEmpty();
    }

    [Fact]
    public async Task UnchangedLegacyCacheRunsFullPassAndPersistsPublication()
    {
        SeedCache(published: false);

        (await Handler(Reporter()).RunAsync()).Should().Be(0);

        _requests.Should().Contain("POST /api/v1/project-profiles/begin");
        _outcomes.Last().Outcomes.Single().Should().Match<ProjectAnalyzerOutcome>(outcome =>
            outcome.State == "succeeded" && outcome.Mode == "full" && outcome.Coverage == "complete");
        var cache = LoadCache();
        var discovery = _snapshots.Single();
        cache.MatchesPublication(Settings().CodeMeridianUrl, 5, discovery.Profile.ObservedAt).Should().BeTrue();
        _requests.Clear();
        (await Handler(Reporter(generation: 5, latest: 5, observedAt: discovery.Profile.ObservedAt)).RunAsync()).Should().Be(0);
        _requests.Should().ContainSingle().Which.Should().StartWith("GET ");
    }

    [Theory]
    [InlineData("unknown", null, null)]
    [InlineData("stale", 3L, 4L)]
    [InlineData("available", 4L, 4L)]
    public async Task UnchangedCacheWithMissingOrReplacedProfileRefreshes(string status, long? generation, long? latest)
    {
        SeedCache();

        (await Handler(Reporter(status, generation, latest)).RunAsync()).Should().Be(0);

        _requests.Should().Contain("POST /api/v1/project-profiles/begin");
        _outcomes.Last().Outcomes.Single().Mode.Should().Be("full");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedReconciliationOrPublicationRetainsCache(bool failPublication)
    {
        SeedCache();
        var before = CacheContents();
        var reporter = Reporter(status: "unknown", failAt: failPublication ? "POST" : "GET");

        (await Handler(reporter).RunAsync()).Should().Be(1);

        CacheContents().Should().Be(before);
        _outcomes.Should().BeEmpty();
    }

    [Fact]
    public async Task MatchingUnchangedCacheDoesNotPublishANewGeneration()
    {
        SeedCache();

        (await Handler(Reporter()).RunAsync()).Should().Be(0);

        _requests.Should().ContainSingle().Which.Should().StartWith("GET ");
        _outcomes.Should().BeEmpty();
    }

    [Theory]
    [InlineData("configuration")]
    [InlineData("implementation")]
    [InlineData("parser")]
    [InlineData("missing")]
    [InlineData("legacy")]
    public async Task ChangedOrMissingAnalysisMetadataRefreshesAnOtherwiseMatchingCache(string metadataChange)
    {
        SeedCache();

        (await Handler(Reporter(metadataChange: metadataChange)).RunAsync()).Should().Be(0);

        _requests.Should().Contain("POST /api/v1/project-profiles/begin");
        _outcomes.Last().Outcomes.Single().Mode.Should().Be("full");
    }

    [Fact]
    public async Task FailedTerminalOutcomePublicationDoesNotAdvanceCache()
    {
        SeedCache();
        var before = CacheContents();

        (await Handler(Reporter(status: "unknown", failAt: "terminal-outcome")).RunAsync()).Should().Be(1);

        _outcomes.SelectMany(snapshot => snapshot.Outcomes).Select(outcome => outcome.State)
            .Should().Equal("running", "succeeded");
        CacheContents().Should().Be(before);
    }

    [Theory]
    [InlineData("available", false)]
    [InlineData("unknown", true)]
    public async Task ReconciliationSelectsAllFilesWhenTheServerProfileIsMissing(string status, bool expectedFull)
    {
        var file = new FileInfo(Path.Combine(_root.FullName, "chapter.md"));
        File.WriteAllText(file.FullName, "# Before");
        var cache = LoadCache();
        cache.Save(cache.BuildPlan(_root, [file], forceFull: true), Settings().CodeMeridianUrl, 3, _observedAt);
        File.WriteAllText(file.FullName, "# After");

        var handler = Handler(Reporter(status));
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var context = typeof(IndexCommandHandler).GetMethod("BuildExecutionContext", flags)!.Invoke(handler, [false]);
        var task = (Task)typeof(IndexCommandHandler).GetMethod("ReconcileExecutionContextAsync", flags)!
            .Invoke(handler, [context, false, CancellationToken.None])!;
        await task;
        var reconciled = task.GetType().GetProperty("Result")!.GetValue(task)!;
        var selection = (IReadOnlyCollection<string>?)reconciled.GetType().GetProperty("ChangedFiles")!.GetValue(reconciled);
        if (expectedFull) selection.Should().BeNull();
        else selection.Should().Equal("chapter.md");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReconciliationPropagatesCancellation(bool published)
    {
        var cache = SeedCache(published);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var action = () => Reporter().IsCacheCurrentAsync(Settings(), cache, cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    private IndexProfileRunReporter Reporter(string status = "available", long? generation = 3, long? latest = 3,
        DateTimeOffset? observedAt = null, string? failAt = null, string? metadataChange = null) => new(new(new RepositoryInventoryDiscovery()))
    {
        HttpClientFactory = (_, _) => new HttpClient(new Respond(async request =>
        {
            _requests.Add($"{request.Method} {request.RequestUri!.AbsolutePath}");
            if (request.Method.Method == failAt) return new(HttpStatusCode.ServiceUnavailable);
            if (request.Method == HttpMethod.Get)
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new
                {
                    status, project = "Docs", generation, latestGeneration = latest,
                    profile = new { observedAt = observedAt ?? _observedAt,
                        configurationFingerprint = metadataChange == "legacy" ? null : metadataChange == "configuration"
                            ? "sha256:" + new string('b', 64) : ProjectAnalysisFingerprints.Configuration(Settings()),
                        analyzers = PublishedAnalyzers(metadataChange) }, contractVersion = "1.0"
                }) };
            if (request.RequestUri.AbsolutePath.EndsWith("/outcomes"))
            {
                _outcomes.Add((await request.Content!.ReadFromJsonAsync<ProjectAnalyzerOutcomeSnapshot>())!);
                if (failAt == "terminal-outcome" && _outcomes.Last().Outcomes.Any(outcome => outcome.State != "running"))
                    return new(HttpStatusCode.ServiceUnavailable);
            }
            if (request.RequestUri.AbsolutePath == "/api/v1/project-profiles/")
                _snapshots.Add((await request.Content!.ReadFromJsonAsync<ProjectProfileSnapshot>())!);
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { generation = 5 }) };
        })) { BaseAddress = new Uri("http://localhost") }
    };

    private ProjectAnalyzerProfile[] PublishedAnalyzers(string? metadataChange) => LocalProjectProfileBuilder.BuildAnalyzers(Settings())
        .Select(analyzer => analyzer.Id != "documents" ? analyzer : analyzer with { Fingerprint = metadataChange switch
        {
            "implementation" => analyzer.Fingerprint! with { Implementation = "sha256:" + new string('b', 64) },
            "parser" => analyzer.Fingerprint! with { Parser = "sha256:" + new string('b', 64) },
            "missing" => null,
            _ => analyzer.Fingerprint
        } }).ToArray();

    private IndexCommandHandler Handler(IndexProfileRunReporter reporter) => new(Options.Create(Settings()),
        new ProjectDiscoveryService(), new IndexerStoragePathService(), new DiagnosticsCommand(new ProjectDiscoveryService()),
        new TypeScriptScopeCatalogWriter(), reporter);

    private ResolvedIndexerSettings Settings() => new()
    {
        RootPath = _root, Project = "Docs", CodeMeridianUrl = "http://localhost", Incremental = true,
        IncludeDocs = true, SkipCSharp = true, SkipTypeScript = true, SkipConfiguration = true, SkipDiagnostics = true,
        StorageMode = IndexerStorageMode.Repository
    };

    private IncrementalIndexCache SeedCache(bool published = true, string? serverUrl = null)
    {
        var cache = LoadCache();
        cache.Save(cache.BuildPlan(_root, [], forceFull: true), published ? serverUrl ?? Settings().CodeMeridianUrl : null,
            published ? 3 : null, published ? _observedAt : null);
        return LoadCache();
    }

    private IncrementalIndexCache LoadCache() => IncrementalIndexCache.Load(
        new IndexerStoragePathService().ResolveCacheDirectory(_root, "Docs", IndexerStorageMode.Repository), "Docs");

    private string CacheContents() => File.ReadAllText(Directory.GetFiles(Path.Combine(_root.FullName, ".meridian", "cache"), "indexer-files-*.json").Single());
    public void Dispose() => _root.Delete(recursive: true);

    private sealed class Respond(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request);
    }
}
