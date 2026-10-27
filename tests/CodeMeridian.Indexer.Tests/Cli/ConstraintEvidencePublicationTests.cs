using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using CodeMeridian.Core.Projects;
using CodeMeridian.Core.RepositoryConstraints;
using CodeMeridian.Indexer.Cli.Commands;
using CodeMeridian.Tooling.Discovery;
using CodeMeridian.Tooling.Storage;
using FluentAssertions;

namespace CodeMeridian.Indexer.Tests.Cli;

public sealed class ConstraintEvidencePublicationTests : IDisposable
{
    private readonly DirectoryInfo root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"constraint-publication-{Guid.NewGuid():N}"));
    private const string Policy = """
        {"repositoryConstraints":{"contractVersion":"1.0","groups":[{"id":"docs","paths":["**/*.md"]}],
        "placementRules":[{"id":"location","group":"docs","allowedPaths":["docs/**"],"reason":"Keep documents under docs.","severity":"error"}],
        "referenceRules":[{"id":"links","sourceGroup":"docs","requireLocalResolution":true,"referenceKinds":["link"],"reason":"Resolve local links.","severity":"error"}]}}
        """;

    [Fact]
    public async Task DefaultPolicyIsNarrowlyAdmittedWithVerifiedRawHashExclusionsAndStructuralFacts()
    {
        var policyPath = Write(".meridian/architecture.json", Policy);
        Write(".meridian/private.json", "{}");
        Write("node_modules/ignored.md", "ignored");
        Write("wrong.md", "[missing](missing.md)");
        ProjectProfileSnapshot? published = null;
        var result = await Reporter(snapshot => published = snapshot).RunAsync(Settings(), _ => Task.FromResult(0));
        result.Should().Be(0);
        published!.Validate().Should().BeNull();
        published.Files.Select(file => file.Path).Should().Equal(".meridian/architecture.json", "wrong.md");
        if (!OperatingSystem.IsWindows())
        {
            published.ConstraintEvidence.Should().BeNull("unverified filesystem case semantics remain unknown");
            return;
        }
        var evidence = published.ConstraintEvidence!;
        evidence.Should().NotBeNull();
        evidence.PolicyFingerprint.Should().Be("sha256:" + Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(policyPath))));
        evidence.ExcludedPaths.Should().Equal(".meridian/private.json", "node_modules");
        var evaluation = RepositoryConstraintEvaluator.Evaluate(published);
        evaluation.Status.Should().Be("evaluated");
        evaluation.Rules.SelectMany(rule => rule.Findings).Select(finding => finding.Status).Should().Equal("misplaced", "missing");
    }

    [Fact]
    public async Task CustomPolicyWithSpacesAndNonAsciiPathsIsAccepted()
    {
        Write("policies/é rules.json", Policy);
        Write("docs/é chapter.md", "");
        var settings = Settings("policies/é rules.json");
        ProjectProfileSnapshot? published = null;
        (await Reporter(snapshot => published = snapshot).RunAsync(settings, _ => Task.FromResult(0))).Should().Be(0);
        published!.Files.Should().Contain(file => file.Path == "policies/é rules.json");
        if (OperatingSystem.IsWindows()) published.ConstraintEvidence!.PolicyPath.Should().Be("policies/é rules.json");
    }

    [Theory]
    [InlineData("policy.json", "{invalid")]
    [InlineData("missing.json", null)]
    [InlineData("policy.json", "{\"repositoryConstraints\":null}")]
    public async Task InvalidOrMissingExplicitPolicyStopsBeforePublicationAndAnalysis(string path, string? content)
    {
        if (content is not null) Write(path, content);
        var published = false;
        var analyzed = false;
        (await Reporter(_ => published = true).RunAsync(Settings(path), _ => { analyzed = true; return Task.FromResult(0); })).Should().Be(1);
        published.Should().BeFalse();
        analyzed.Should().BeFalse();
    }

    [Fact]
    public async Task NamespaceOnlyPoliciesAndAbsentDefaultsDoNotInventRepositoryRules()
    {
        ProjectProfileSnapshot? published = null;
        var reporter = Reporter(snapshot => published = snapshot);
        (await reporter.RunAsync(Settings(), _ => Task.FromResult(0))).Should().Be(0);
        published!.ConstraintEvidence.Should().BeNull();
        Write(".meridian/architecture.json", """
            {"layers":[{"id":"core","namespaceContainsAny":["Core"]}],"forbiddenDependencies":[{"from":"core","to":"core"}]}
            """);
        (await reporter.RunAsync(Settings(), _ => Task.FromResult(0))).Should().Be(0);
        published!.ConstraintEvidence.Should().BeNull();
    }

    [Fact]
    public async Task ChangedPolicyOrExclusionInvalidatesPublishedGenerationEvenWithNoAnalyzerOutcomes()
    {
        if (!OperatingSystem.IsWindows()) return; // No case-bound evidence on unaudited filesystems.
        Write(".meridian/architecture.json", Policy);
        var reservations = 0;
        var reporter = Reporter(_ => { }, () => reservations++);
        (await reporter.RunAsync(Settings(), _ =>
        {
            Write("node_modules/new.md", "excluded");
            return Task.FromResult(0);
        })).Should().Be(2);
        reservations.Should().Be(2);
    }

    [Fact]
    public void AdmissionCannotEscapeTheRepositoryAndMissingDefaultsDoNotCreateFiles()
    {
        var discover = new RepositoryInventoryDiscovery();
        FluentActions.Invoking(() => discover.Discover(root, admittedPaths: ["../outside.json"]))
            .Should().Throw<ArgumentException>();
        var inventory = discover.Discover(root, admittedPaths: [".meridian/architecture.json"]);
        inventory.IsComplete.Should().BeTrue();
        inventory.Files.Should().BeEmpty();
    }

    [Theory]
    [InlineData("unchanged", true)]
    [InlineData("policy", false)]
    [InlineData("asset", false)]
    [InlineData("excluded", false)]
    [InlineData("deleted-policy", false)]
    public async Task NoChangeShortcutReconcilesPolicyAssetsAndExclusionsWithoutBlockingDeltaSelection(string change, bool expected)
    {
        Write(".meridian/architecture.json", Policy);
        Write(".meridian/cache/placeholder", "");
        var settings = new ResolvedIndexerSettings
        {
            RootPath = root, Project = "Docs", CodeMeridianUrl = "http://localhost", StorageMode = IndexerStorageMode.Repository,
            SkipCSharp = true, SkipTypeScript = true, SkipConfiguration = true
        };
        var builder = new LocalProjectProfileBuilder(new RepositoryInventoryDiscovery());
        var snapshot = builder.BuildSnapshot(settings, 3, out var inventory);
        var source = AnalyzedSourceSnapshot.Capture(root, snapshot.Files, CancellationToken.None);
        snapshot = snapshot with { Profile = snapshot.Profile with { SourceFingerprint = source.Fingerprint } };
        var evidence = await ConstraintEvidenceBuilder.BuildAsync(settings, snapshot, inventory, source, CancellationToken.None);
        var cache = IncrementalIndexCache.Load(new IndexerStoragePathService().ResolveCacheDirectory(root, "Docs", IndexerStorageMode.Repository), "Docs");
        cache.Save(cache.BuildPlan(root, [], forceFull: true), settings.CodeMeridianUrl, 3, snapshot.Profile.ObservedAt);
        var reporter = new IndexProfileRunReporter(builder)
        {
            HttpClientFactory = (_, _) => new HttpClient(new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    status = "available", project = "Docs", generation = 3, latestGeneration = 3, snapshot.Profile,
                    repositoryConstraints = new { evidenceFingerprint = evidence?.Fingerprint }, contractVersion = "1.0"
                })
            }))) { BaseAddress = new Uri("http://localhost") }
        };
        if (change == "policy") Write(".meridian/architecture.json", Policy.Replace("Resolve local links.", "Require local links.", StringComparison.Ordinal));
        if (change == "asset") Write("new.png", "asset");
        if (change == "excluded") Write("node_modules/new.md", "excluded");
        if (change == "deleted-policy") File.Delete(Path.Combine(root.FullName, ".meridian/architecture.json"));
        (await reporter.IsCacheCurrentAsync(settings, cache, verifyInventory: true))
            .Should().Be(change == "excluded" && !OperatingSystem.IsWindows() ? true : expected);
        (await reporter.IsCacheCurrentAsync(settings, cache)).Should().BeTrue("ordinary delta selection still uses matching analysis metadata");
    }

    private string Write(string path, string content)
    {
        var absolute = Path.Combine(root.FullName, path);
        Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
        File.WriteAllText(absolute, content);
        return absolute;
    }

    private ResolvedIndexerSettings Settings(string? architecturePath = null) => new()
    {
        RootPath = root, Project = "Docs", CodeMeridianUrl = "http://localhost", StorageMode = IndexerStorageMode.Repository,
        ArchitecturePath = architecturePath
    };

    private static IndexProfileRunReporter Reporter(Action<ProjectProfileSnapshot> published, Action? reserved = null) => new(new(new RepositoryInventoryDiscovery()))
    {
        WorkerFingerprintReader = (_, _) => Task.FromResult<ProjectAnalyzerFingerprint?>(null),
        HttpClientFactory = (_, _) => new HttpClient(new Handler(async request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/begin", StringComparison.Ordinal)) reserved?.Invoke();
            if (request.RequestUri.AbsolutePath == "/api/v1/project-profiles/")
                published((await request.Content!.ReadFromJsonAsync<ProjectProfileSnapshot>())!);
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { generation = 3 }) };
        })) { BaseAddress = new Uri("http://localhost") }
    };

    public void Dispose() => root.Delete(recursive: true);

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request);
    }
}
