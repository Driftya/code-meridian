using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CodeMeridian.Core.Projects;
using CodeMeridian.Indexer.Cli.Commands;
using CodeMeridian.Indexer.Cli.Composition;
using CodeMeridian.Indexer.Cli.Configuration;
using CodeMeridian.Tooling.Discovery;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace CodeMeridian.Indexer.Tests.Cli;

[Collection(EnvironmentVariableTestCollection.Name)]
public sealed class ProjectProfilePublicationTests
{
    [Fact]
    public async Task FailedUploadReportsFailureWithoutUploadedEvidence()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"profile-failed-{Guid.NewGuid():N}"));
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var output = new StringWriter();
        using var error = new StringWriter();
        try
        {
            var services = new ServiceCollection().AddIndexerCli();
            await using var provider = services.BuildServiceProvider();
            var command = new ProjectProfileCommand(provider.GetRequiredService<IndexCommandSettingsFactory>(),
                provider.GetRequiredService<LocalProjectProfileBuilder>())
            {
                HttpClientFactory = (_, _) => new HttpClient(new Handler(request => Task.FromResult(
                    request.RequestUri!.AbsolutePath.EndsWith("/begin")
                        ? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { generation = 1 }) }
                        : new HttpResponseMessage(HttpStatusCode.Conflict)))) { BaseAddress = new Uri("http://localhost") }
            };
            Console.SetOut(output);
            Console.SetError(error);
            (await command.RunAsync(root.FullName, "Docs", "json", false, false, false, false, false, publish: true))
                .Should().Be(1);
            output.ToString().Should().BeEmpty();
            error.ToString().Should().Contain("409");
            Directory.Exists(Path.Combine(root.FullName, ".meridian")).Should().BeFalse();
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task PublicationReservesBeforeDiscoveryAndUploadsCompleteInventory()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"profile-publish-{Guid.NewGuid():N}"));
        var originalOut = Console.Out;
        using var output = new StringWriter();
        try
        {
            for (var i = 0; i < 8; i++) File.WriteAllText(Path.Combine(root.FullName, $"{i}.md"), "# Docs");
            var services = new ServiceCollection().AddIndexerCli();
            await using var provider = services.BuildServiceProvider();
            var requests = 0;
            ProjectProfileSnapshot? uploaded = null;
            var command = new ProjectProfileCommand(provider.GetRequiredService<IndexCommandSettingsFactory>(),
                provider.GetRequiredService<LocalProjectProfileBuilder>())
            {
                HttpClientFactory = (_, _) => new HttpClient(new Handler(async request =>
                {
                    requests++;
                    if (requests == 1)
                    {
                        // This file must be observed: generation reservation precedes discovery.
                        File.WriteAllText(Path.Combine(root.FullName, "reserved.md"), "# Reserved");
                        return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { generation = 9 }) };
                    }
                    uploaded = await request.Content!.ReadFromJsonAsync<ProjectProfileSnapshot>();
                    return new(HttpStatusCode.OK);
                })) { BaseAddress = new Uri("http://localhost") }
            };
            Console.SetOut(output);
            var exit = await command.RunAsync(root.FullName, "Docs", "json", false, false, false, false, false, publish: true);
            exit.Should().Be(0);
            requests.Should().Be(2);
            uploaded!.Generation.Should().Be(9);
            uploaded.Files.Should().HaveCount(9);
            uploaded.Profile.FileKinds.Single().ExamplePaths.Should().HaveCount(5);
            uploaded.Validate().Should().BeNull();
            JsonSerializer.Deserialize<ProjectProfile>(output.ToString(), ProjectProfileCommand.JsonOptions)!.EvidenceSource
                .Should().Be("uploaded_discovery");
        }
        finally
        {
            Console.SetOut(originalOut);
            root.Delete(recursive: true);
        }
    }

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => respond(request);
    }
}
