using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CodeMeridian.Indexer.Cli.Commands;
using CodeMeridian.Sdk;
using CodeMeridian.Tooling.Configuration;
using FluentAssertions;

namespace CodeMeridian.Indexer.Tests.Cli;

public sealed class PrContextReportCommandTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateDirectory(Path.Combine(
        Path.GetTempPath(),
        $"codemeridian-pr-context-report-{Guid.NewGuid():N}"));

    [Fact]
    public async Task RunAsync_WithMarkdownFormat_PrintsReport()
    {
        var output = new StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(output);

        try
        {
            var sut = new PrContextReportCommand(
                new StubToolConfigurationService(_root),
                new StubGitDiffProvider(["src/Subscriptions/SubscriptionService.cs"]),
                (_, _) => new HttpClient(new StubHandler())
                {
                    BaseAddress = new Uri("http://localhost")
                });

            var exitCode = await sut.RunAsync(new PrContextReportCommandOptions(
                _root.FullName,
                "CodeMeridian",
                "http://localhost",
                "origin/main",
                "HEAD",
                IncludeDocs: true,
                Format: "markdown",
                OutputPath: null));

            exitCode.Should().Be(0);
            output.ToString().Should().Contain("# PR Context Report");
            output.ToString().Should().Contain("## Related Documentation");
            output.ToString().Should().Contain("docs/features/subscriptions.md");
        }
        finally
        {
            Console.SetOut(originalOut);
            output.Dispose();
        }
    }

    [Fact]
    public async Task RunAsync_WithJsonFormatAndOutput_WritesFile()
    {
        var sut = new PrContextReportCommand(
            new StubToolConfigurationService(_root),
            new StubGitDiffProvider(["src/Subscriptions/SubscriptionService.cs"]),
            (_, _) => new HttpClient(new StubHandler())
            {
                BaseAddress = new Uri("http://localhost")
            });

        var exitCode = await sut.RunAsync(new PrContextReportCommandOptions(
            _root.FullName,
            "CodeMeridian",
            "http://localhost",
            "origin/main",
            "HEAD",
            IncludeDocs: true,
            Format: "json",
            OutputPath: "artifacts/pr-context.json"));

        exitCode.Should().Be(0);
        var outputPath = Path.Combine(_root.FullName, "artifacts", "pr-context.json");
        File.Exists(outputPath).Should().BeTrue();
        var payload = JsonSerializer.Deserialize<JsonElement>(await File.ReadAllTextAsync(outputPath));
        payload.GetProperty("projectContext").GetString().Should().Be("CodeMeridian");
        payload.GetProperty("changedFiles")[0].GetString().Should().Be("src/Subscriptions/SubscriptionService.cs");
    }

    [Fact]
    public async Task RunAsync_WhenGitDiffFails_ReturnsError()
    {
        var error = new StringWriter();
        var originalError = Console.Error;
        Console.SetError(error);

        try
        {
            var sut = new PrContextReportCommand(
                new StubToolConfigurationService(_root),
                new ThrowingGitDiffProvider(),
                (_, _) => new HttpClient(new StubHandler())
                {
                    BaseAddress = new Uri("http://localhost")
                });

            var exitCode = await sut.RunAsync(new PrContextReportCommandOptions(
                _root.FullName,
                "CodeMeridian",
                "http://localhost",
                "origin/main",
                "HEAD",
                IncludeDocs: true,
                Format: "markdown",
                OutputPath: null));

            exitCode.Should().Be(1);
            error.ToString().Should().Contain("git diff failed");
        }
        finally
        {
            Console.SetError(originalError);
            error.Dispose();
        }
    }

    public void Dispose()
    {
        if (_root.Exists)
            _root.Delete(recursive: true);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new PrContextReportResponse(
                    "CodeMeridian",
                    "origin/main",
                    "HEAD",
                    ["src/Subscriptions/SubscriptionService.cs"],
                    [new PrContextNodeSummaryResponse("node-1", "SubscriptionService.SyncAsync", "Method", "src/Subscriptions/SubscriptionService.cs", "CodeMeridian", 10, 25)],
                    [new PrContextImpactSummaryResponse(
                        new PrContextNodeSummaryResponse("node-2", "SubscriptionController", "Class", "src/Api/SubscriptionController.cs", "CodeMeridian", 4, 30),
                        1,
                        1)],
                    [new PrContextNodeSummaryResponse("node-1", "SubscriptionService.SyncAsync", "Method", "src/Subscriptions/SubscriptionService.cs", "CodeMeridian", 10, 25)],
                    [],
                    [new PrContextRelatedDocumentResponse("doc-1", "docs/features/subscriptions.md", "High", 9.1d, ["subscription", "badge"])],
                    ["Review the subscription path."]))
            });
    }

    private sealed class StubGitDiffProvider(IReadOnlyCollection<string> changedFiles) : IPrContextGitDiffProvider
    {
        public Task<IReadOnlyCollection<string>> GetChangedFilesAsync(DirectoryInfo root, string baseRef, string headRef, CancellationToken cancellationToken) =>
            Task.FromResult(changedFiles);
    }

    private sealed class ThrowingGitDiffProvider : IPrContextGitDiffProvider
    {
        public Task<IReadOnlyCollection<string>> GetChangedFilesAsync(DirectoryInfo root, string baseRef, string headRef, CancellationToken cancellationToken) =>
            Task.FromException<IReadOnlyCollection<string>>(new InvalidOperationException("git diff failed: boom"));
    }

    private sealed class StubToolConfigurationService(DirectoryInfo root) : IToolConfigurationService
    {
        public ToolConfigurationContext CreateContext(string? path) =>
            new(root, null, null, "CodeMeridian", "http://localhost", null);

        public string ResolveProject(ToolConfigurationContext context, string? overrideProject, bool includeFallback = true) =>
            overrideProject ?? context.EnvironmentProject ?? "CodeMeridian";

        public string ResolveCodeMeridianUrl(ToolConfigurationContext context, string? overrideUrl) =>
            overrideUrl ?? context.EnvironmentUrl ?? "http://localhost";

        public bool ResolveAllowRepoScripts(ToolConfigurationContext context, bool allowRepoScriptsOverride) => allowRepoScriptsOverride;

        public DirectoryInfo ResolveRootPath(string? path) => root;
    }
}
