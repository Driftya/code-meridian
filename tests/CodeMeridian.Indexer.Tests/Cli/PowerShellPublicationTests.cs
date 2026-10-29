using System.Net;
using System.Net.Http.Json;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Projects;
using CodeMeridian.Indexer.Cli.Commands;
using CodeMeridian.Indexer.Cli.Configuration;
using CodeMeridian.Tooling.Configuration;
using CodeMeridian.Tooling.Discovery;
using CodeMeridian.Tooling.Storage;
using FluentAssertions;
using NSubstitute;
using CodeMeridian.Tooling.Watching;
using Microsoft.Extensions.Logging.Abstractions;
using CodeMeridian.Application.Projects;
using CodeMeridian.Application.Services;
using CodeMeridian.Core.Knowledge;
using Microsoft.Extensions.Options;

namespace CodeMeridian.Indexer.Tests.Cli;

public sealed partial class PowerShellPublicationTests : IDisposable
{
    private readonly DirectoryInfo root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "powershell CLI å " + Guid.NewGuid().ToString("N")));
    private readonly List<PowerShellGraphSnapshot> graphs = [];
    private readonly List<ProjectAnalyzerOutcomeSnapshot> outcomes = [];
    private int generation;
    private ProjectProfileSnapshot? profile;

    [Theory]
    [InlineData("context")]
    [InlineData("impact")]
    [InlineData("connection")]
    [InlineData("snippet")]
    public async Task NativePublishedFactsDoNotAuthorizeUnauditedGenericQueries(string query)
    {
        Write("main.ps1", "function Work {}; Work");
        (await Run()).Should().Be(0);
        var method = graphs.Single().Files.Single().Nodes.Single(node => node.Name == "Work");
        method.SourceSnippet.Should().Be("function Work {}");
        var profiles = Substitute.For<IProjectProfileRepository>();
        var workerProfile = profile!.Profile;
        // Even a complete unrelated C# producer must not authorize PowerShell's Method nodes.
        workerProfile = workerProfile with { FileCount = workerProfile.FileCount + 1,
            FileKinds = [.. workerProfile.FileKinds, new("csharp", 1, ["Other.cs"])],
            Analyzers = workerProfile.Analyzers.Select(analyzer => analyzer.Id == "csharp" ? analyzer with { ConfigurationState = "enabled" } : analyzer).ToArray() };
        profiles.GetAsync("Fixture", Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(new StoredProjectProfile(
            generation, generation, DateTimeOffset.UtcNow, workerProfile, new("main.ps1", "powershell"))
            { AnalyzerOutcomes = [.. outcomes.Last().Outcomes, new("csharp", ".", "succeeded", "complete", "full", DateTimeOffset.UtcNow, "fixture/1", [])
                { SourceFingerprint = workerProfile.SourceFingerprint, Fingerprint = workerProfile.Analyzers.Single(analyzer => analyzer.Id == "csharp").Fingerprint }] });
        var graph = Substitute.For<ICodeGraphRepository>();
        graph.GetContextForEditingAsync(method.Id, Arg.Any<CancellationToken>()).Returns(new EditingContext(method, [], [], []));
        var service = new CodebaseQueryService(graph, Substitute.For<IVectorRepository>(), new NoOpEmbeddingProvider(),
            Options.Create(new CodebaseAnalysisOptions()), Options.Create(new CodebaseIndexingOptions()),
            new DefaultAnalysisProfilePolicy(), Substitute.For<IProjectAnalysisOptionsResolver>(), new ProjectProfileService(profiles));

        var result = query switch
        {
            "context" => await service.GetContextForEditingAsync(method.Id),
            "impact" => await service.FindImpactAsync(method.Id),
            "connection" => await service.FindConnectionAsync(method.Id, method.Id),
            _ => (await service.BuildMinimalContextResultAsync(method.Id, includeSourceSnippets: true)).ToMarkdown(ContextDetailLevel.Compact)
        };

        result.Should().Contain("unsupported").And.NotContain("function Work {}");
        await graph.DidNotReceiveWithAnyArgs().QueryNodesAsync(default!, default);
        await graph.DidNotReceiveWithAnyArgs().FindImpactAsync(default!, default, default);
        await graph.DidNotReceiveWithAnyArgs().FindConnectionAsync(default!, default!, default);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeFactsCannotLeakThroughVerifiedCallableNeighbours(bool minimalContext)
    {
        Write("main.ps1", "function Work {}; Work");
        (await Run()).Should().Be(0);
        var method = graphs.Single().Files.Single().Nodes.Single(node => node.Name == "Work");
        var target = method with { Id = "Fixture::Method::Caller", Name = "Caller", FilePath = "Caller.cs", SourceSnippet = null };
        var profiles = Substitute.For<IProjectProfileRepository>();
        var workerProfile = profile!.Profile with { FileCount = 2,
            FileKinds = [.. profile.Profile.FileKinds, new("csharp", 1, ["Caller.cs"])],
            Analyzers = profile.Profile.Analyzers.Select(analyzer => analyzer.Id == "csharp" ? analyzer with { ConfigurationState = "enabled" } : analyzer).ToArray() };
        profiles.GetAsync("Fixture", Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(call => new StoredProjectProfile(
            generation, generation, DateTimeOffset.UtcNow, workerProfile,
            call.ArgAt<string>(1) == "main.ps1" ? new("main.ps1", "powershell") : new("Caller.cs", "csharp"))
            { AnalyzerOutcomes = [.. outcomes.Last().Outcomes, new("csharp", ".", "succeeded", "complete", "full", DateTimeOffset.UtcNow, "fixture/1", [])
                { SourceFingerprint = workerProfile.SourceFingerprint, Fingerprint = workerProfile.Analyzers.Single(analyzer => analyzer.Id == "csharp").Fingerprint }] });
        var graph = Substitute.For<ICodeGraphRepository>();
        graph.GetContextForEditingAsync(target.Id, Arg.Any<CancellationToken>()).Returns(new EditingContext(target, [method], [], []));
        graph.GetContextForEditingAsync(method.Id, Arg.Any<CancellationToken>()).Returns(new EditingContext(method, [], [], []));
        graph.FindImpactAsync(target.Id, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([(method, 1)]);
        var service = new CodebaseQueryService(graph, Substitute.For<IVectorRepository>(), new NoOpEmbeddingProvider(),
            Options.Create(new CodebaseAnalysisOptions()), Options.Create(new CodebaseIndexingOptions()),
            new DefaultAnalysisProfilePolicy(), Substitute.For<IProjectAnalysisOptionsResolver>(), new ProjectProfileService(profiles));

        var result = minimalContext
            ? (await service.BuildMinimalContextResultAsync(target.Id, includeTests: false, includeSourceSnippets: true)).ToMarkdown(ContextDetailLevel.Full)
            : await service.FindImpactAsync(target.Id);

        result.Should().Contain("status: unknown").And.NotContain("function Work {}");
    }

    [Fact]
    public async Task RealIsolatedWorkerRebindsUnchangedConsumersAndReconcilesDeletionsWithoutExecution()
    {
        Write("helper.ps1", "function Work {};");
        Write("main.ps1", ". \"$PSScriptRoot/helper.ps1\"; Work; [IO.File]::WriteAllText(\"$PSScriptRoot/executed\", 'bad')");
        Write("node_modules/ignored.ps1", "function {");
        (await Run()).Should().Be(0);
        graphs.Single().Files.Select(file => file.Path).Should().BeEquivalentTo("helper.ps1", "main.ps1");
        graphs.Single().Files.SelectMany(file => file.Edges).Count(edge => edge.Type == CodeEdgeType.Calls).Should().Be(1);
        Write("helper.ps1", "function Renamed {};");
        (await Run()).Should().Be(0);
        graphs.Last().Files.SelectMany(file => file.Edges).Should().NotContain(edge => edge.Type == CodeEdgeType.Calls);
        File.Delete(Path.Combine(root.FullName, "helper.ps1"));
        (await Run()).Should().Be(2);
        graphs.Last().Files.Should().ContainSingle().Which.Path.Should().Be("main.ps1");
        File.Exists(Path.Combine(root.FullName, "executed")).Should().BeFalse();
        outcomes.SelectMany(report => report.Outcomes).Should().OnlyContain(outcome => outcome.Mode == "full");
        outcomes.First().Outcomes.Single().Coverage.Should().Be("unknown", "running is not coverage");
        outcomes[1].Outcomes.Single().Coverage.Should().Be("complete", "only PowerShell definition coverage is advertised");
        outcomes.Last().Outcomes.Single().Coverage.Should().Be("partial");
        graphs.Should().OnlyContain(graph => graph.Validate() == null && graph.Generation > 0);
    }

    [Fact]
    public async Task ParseFailurePublishesPartialOutcomeAndTransportFailureCannotReturnSuccess()
    {
        Write("bad.ps1", "function {");
        (await Run()).Should().Be(2);
        outcomes.Last().Outcomes.Single().State.Should().Be("partial");
        outcomes.Last().Outcomes.Single().Coverage.Should().Be("partial");
        Write("bad.ps1", "function Good {};");
        (await Run(failUpload: true)).Should().Be(1);
        outcomes.Last().Outcomes.Single().State.Should().Be("failed");
    }

    [Theory]
    [InlineData("worker")]
    [InlineData("source")]
    [InlineData("inventory")]
    public async Task UnverifiedWorkerReadsCannotBeUploadedEvenWhenFilesAreRestored(string mismatch)
    {
        Write("main.ps1", "function Work {};");
        var settings = Settings();
        var reporter = Reporter();
        var result = await reporter.RunAsync(settings, report => report!.RunAnalyzerAsync("powershell", ["."], async () =>
        {
            var hash = "sha256:" + new string('a', 64);
            var path = mismatch == "inventory" ? "other.ps1" : "main.ps1";
            var snapshot = new PowerShellGraphSnapshot(settings.Project, report.Generation, report.SourceFingerprint,
                mismatch == "worker" ? new(hash, hash) : report.Fingerprint("powershell")!,
                [new(path, "complete", hash, [new() { Id = "Fixture::PowerShellFile::main", Name = path,
                    Type = CodeNodeType.File, FilePath = path, SourceHash = hash, ProjectContext = settings.Project }], [])]);
            await report.PublishPowerShellAsync(snapshot, CancellationToken.None);
            return 0;
        }, true));
        result.Should().Be(1);
        graphs.Should().BeEmpty();
        outcomes.Last().Outcomes.Single().State.Should().Be("failed");
    }

    [Theory]
    [InlineData(true, false, false, true)]
    [InlineData(true, false, true, false)]
    [InlineData(true, true, false, false)]
    [InlineData(false, false, false, false)]
    public void SkipAndExternalScopeOverrideExplicitOptIn(bool enabled, bool external, bool skip, bool expected)
    {
        var settings = new ResolvedIndexerSettings { RootPath = root, Project = "Fixture", CodeMeridianUrl = "http://localhost",
            StorageMode = IndexerStorageMode.Repository, PowerShell = new() { Enabled = enabled }, ExternalOnly = external, SkipPowerShell = skip };
        PowerShellIndexRunCoordinator.IsEnabled(settings).Should().Be(expected);
        var analyzer = LocalProjectProfileBuilder.BuildAnalyzers(settings).Single(item => item.Id == "powershell");
        analyzer.ConfigurationState.Should().Be(expected ? "enabled" : "disabled");
        analyzer.SupportedCapabilities.Should().Equal("powershell_definitions");
    }

    [Fact]
    public void LocalDisabledConfigurationOverridesGlobalOptInAndSkipOverridesBoth()
    {
        var service = Substitute.For<IToolConfigurationService>();
        var global = new CodeMeridianConfigSnapshot(null, null, null, null, null, null, null, 2, PowerShell: new() { Enabled = true });
        var local = global with { PowerShell = new() { Enabled = false } };
        service.CreateContext(Arg.Any<string?>()).Returns(new ToolConfigurationContext(root, local, global, null, null, null));
        service.ResolveProject(Arg.Any<ToolConfigurationContext>(), Arg.Any<string?>()).Returns("Fixture");
        service.ResolveCodeMeridianUrl(Arg.Any<ToolConfigurationContext>(), Arg.Any<string?>()).Returns("http://localhost");
        var options = new IndexCommandOptions(root.FullName, null, null, false, false, false, false, false, false,
            true, true, true, true, false, true, null, SkipPowerShell: true);
        var settings = new IndexCommandSettingsFactory(service).Create(options);
        settings.PowerShell!.Enabled.Should().BeFalse();
        settings.SkipPowerShell.Should().BeTrue();
    }

    [Fact]
    public void ConfigurationSchemaLoadsOptInAndRejectsUnknownPowerShellSettings()
    {
        Write("meridian.json", """{"version":2,"indexing":{"powershell":{"enabled":true,"caseSensitive":false}}}""");
        var store = new CodeMeridianConfigFileStore();
        var options = store.LoadLocal(root)!.PowerShell;
        options!.Enabled.Should().BeTrue();
        options.CaseSensitive.Should().BeFalse();
        Write("meridian.json", """{"version":2,"indexing":{"powershell":{"execute":true}}}""");
        var load = () => store.LoadLocal(root);
        load.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task WatchBatchReanalyzesTheCompletePowerShellSelection()
    {
        Write("main.ps1", "function First {};");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var completed = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var loop = new IndexWatchLoop(root, NullLogger.Instance, TimeSpan.FromMilliseconds(30));
        var watch = loop.RunAsync(async (_, _) => completed.TrySetResult(await Run()), cancellation.Token);
        try
        {
            Write("main.ps1", "function Changed {};");
            (await completed.Task.WaitAsync(cancellation.Token)).Should().Be(0);
            graphs.Last().Files.Single().Nodes.Should().Contain(node => node.Name == "Changed");
        }
        finally { cancellation.Cancel(); await watch; }
    }

    private async Task<int> Run(bool failUpload = false) => await Reporter(failUpload).RunAsync(Settings(),
        report => report!.RunAnalyzerAsync("powershell", ["."], () => PowerShellIndexRunCoordinator.RunAsync(Settings(), report), true));

    private IndexProfileRunReporter Reporter(bool failUpload = false) => new(new(new RepositoryInventoryDiscovery()))
    {
        HttpClientFactory = (_, _) => new HttpClient(new Respond(async request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/begin")) generation++;
            if (path == "/api/v1/project-profiles/") profile = await request.Content!.ReadFromJsonAsync<ProjectProfileSnapshot>();
            if (path.EndsWith("/outcomes")) outcomes.Add((await request.Content!.ReadFromJsonAsync<ProjectAnalyzerOutcomeSnapshot>())!);
            if (path.EndsWith("/powershell"))
            {
                graphs.Add((await request.Content!.ReadFromJsonAsync<PowerShellGraphSnapshot>())!);
                if (failUpload) return new(HttpStatusCode.ServiceUnavailable);
            }
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { generation }) };
        })) { BaseAddress = new("http://localhost") }
    };

    private ResolvedIndexerSettings Settings() => new()
    {
        RootPath = root, Project = "Fixture", CodeMeridianUrl = "http://localhost", StorageMode = IndexerStorageMode.Repository,
        SkipCSharp = true, SkipTypeScript = true, SkipConfiguration = true, IncludeDocs = false,
        SkipDiagnostics = true, PowerShell = new() { Enabled = true, CaseSensitive = OperatingSystem.IsWindows() ? null : true }
    };

    private void Write(string path, string content)
    {
        var file = new FileInfo(Path.Combine(root.FullName, path));
        file.Directory!.Create(); File.WriteAllText(file.FullName, content);
    }

    public void Dispose() => root.Delete(true);
    private sealed class Respond(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request);
    }
}
