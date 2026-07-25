using System.Net;
using System.Net.Http.Json;
using CodeMeridian.Sdk;
using CodeMeridian.Indexer.Cli.Commands;
using CodeMeridian.Tooling.Discovery;
using FluentAssertions;
using NSubstitute;

namespace CodeMeridian.Indexer.Tests.Cli;

public sealed class DiagnosticsCommandTests
{
    [Fact]
    public void BuildDotnetBuildArguments_UsesIsolatedOutputPaths()
    {
        var rootPath = new DirectoryInfo(Path.Combine("C:", "repo"));

        var arguments = DiagnosticsCommand.BuildDotnetBuildArguments(rootPath);

        arguments.Should().ContainInOrder("build", "--no-restore", "--nologo");
        arguments.Should().Contain(argument => argument.StartsWith("-p:BaseOutputPath=", StringComparison.Ordinal));
        arguments.Should().NotContain(argument =>
            string.Equals(argument, "-p:BaseOutputPath=" + Path.Combine(rootPath.FullName, "bin"), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RunAsync_WithNoNewFindings_ClearsAndVerifiesOrdinaryDiagnostics()
    {
        var discovery = Substitute.For<IProjectDiscoveryService>();
        var handler = new DiagnosticsLifecycleHandler();
        var sut = new DiagnosticsCommand(
            discovery,
            (url, _) => new HttpClient(handler) { BaseAddress = new Uri(url) });

        var exitCode = await sut.RunAsync(
            new DirectoryInfo(Path.GetTempPath()),
            [],
            "Project",
            "http://localhost",
            null,
            allowRepoScripts: false);

        exitCode.Should().Be(0);
        handler.Requests.Should().ContainSingle(request =>
            request.Method == HttpMethod.Delete
            && request.Path == "/api/v1/knowledge/project/Project/diagnostics");
        handler.Requests.Should().Contain(request =>
            request.Method == HttpMethod.Get
            && request.Path == "/api/v1/status/doctor");
        handler.Requests.Should().NotContain(request => request.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task RunAsync_WhenCleanupFails_StopsBeforeVerificationOrIngestion()
    {
        var discovery = Substitute.For<IProjectDiscoveryService>();
        var handler = new DiagnosticsLifecycleHandler(failCleanup: true);
        var sut = new DiagnosticsCommand(
            discovery,
            (url, _) => new HttpClient(handler) { BaseAddress = new Uri(url) });

        var action = () => sut.RunAsync(
            new DirectoryInfo(Path.GetTempPath()),
            [],
            "Project",
            "http://localhost",
            null,
            allowRepoScripts: false);

        await action.Should().ThrowAsync<HttpRequestException>();
        handler.Requests.Should().ContainSingle();
    }

    private sealed class DiagnosticsLifecycleHandler(bool failCleanup = false) : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Path)> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add((request.Method, request.RequestUri!.AbsolutePath));
            if (request.Method == HttpMethod.Delete)
            {
                return Task.FromResult(failCleanup
                    ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                    : new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = JsonContent.Create(new { deletedCount = 2 })
                    });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new DoctorStatusResponse(
                    "Project",
                    true,
                    10,
                    2,
                    0,
                    0,
                    "low",
                    "Graph drift: low",
                    false,
                    "none",
                    0,
                    null))
            });
        }
    }
}
