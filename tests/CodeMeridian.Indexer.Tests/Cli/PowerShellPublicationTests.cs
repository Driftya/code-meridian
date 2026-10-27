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

namespace CodeMeridian.Indexer.Tests.Cli;

public sealed class PowerShellPublicationTests : IDisposable
{
    private readonly DirectoryInfo root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "powershell CLI å " + Guid.NewGuid().ToString("N")));
    private readonly List<PowerShellGraphSnapshot> graphs = [];
    private readonly List<ProjectAnalyzerOutcomeSnapshot> outcomes = [];
    private int generation;

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
        outcomes.SelectMany(report => report.Outcomes).Should().OnlyContain(outcome => outcome.Coverage != "complete" && outcome.Mode == "full");
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
        analyzer.SupportedCapabilities.Should().BeEmpty();
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
