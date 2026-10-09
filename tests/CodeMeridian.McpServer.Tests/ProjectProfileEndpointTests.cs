using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CodeMeridian.Core.Projects;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModelContextProtocol.Protocol;
using Json.Schema;
using NSubstitute;

namespace CodeMeridian.McpServer.Tests;

public sealed class ProjectProfileEndpointTests
{
    [Fact]
    public async Task ApiRequiresAuthenticationReservesPublishesAndReturnsUnknown()
    {
        var repository = Substitute.For<IProjectProfileRepository>();
        repository.BeginAsync("Docs", Arg.Any<CancellationToken>()).Returns(7L);
        await using var factory = new GraphQlWebApplicationFactory().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IProjectProfileRepository>();
            services.AddSingleton(repository);
        }));
        using var client = factory.CreateClient();
        (await client.PostAsJsonAsync("/api/v1/project-profiles/begin", new { projectContext = "Docs" }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        client.DefaultRequestHeaders.Authorization = new("Bearer", GraphQlWebApplicationFactory.ApiKey);
        var begin = await client.PostAsJsonAsync("/api/v1/project-profiles/begin", new { projectContext = "Docs" });
        (await begin.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("generation").GetInt64().Should().Be(7);
        var snapshot = new ProjectProfileSnapshot(7, new("Docs", DateTimeOffset.UtcNow, "complete", 0, [], [], 0, [], [], 0), []);
        (await client.PostAsJsonAsync("/api/v1/project-profiles/", snapshot)).StatusCode.Should().Be(HttpStatusCode.OK);
        await repository.Received(1).PublishAsync(Arg.Is<ProjectProfileSnapshot>(s => s.Generation == 7), Arg.Any<CancellationToken>());
        (await client.PostAsJsonAsync("/api/v1/project-profiles/", snapshot with { Profile = snapshot.Profile with { DiscoveryState = "partial" } }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var response = await client.GetFromJsonAsync<JsonElement>("/api/v1/project-profiles/?projectContext=Docs&targetPath=README.md");
        response.GetProperty("status").GetString().Should().Be("unknown");
        (await client.GetAsync("/api/v1/project-profiles/?projectContext=Docs&targetPath=..%2Fsecret.md"))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        repository.PublishAsync(Arg.Any<ProjectProfileSnapshot>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new ProjectProfileConflictException("stale generation"));
        (await client.PostAsJsonAsync("/api/v1/project-profiles/", snapshot)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task McpReturnsStructuredUnknownWithTextFallback()
    {
        var repository = Substitute.For<IProjectProfileRepository>();
        await using var factory = new GraphQlWebApplicationFactory().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IProjectProfileRepository>();
            services.AddSingleton(repository);
        }));
        using var http = factory.CreateClient();
        await using var client = await McpTestClient.CreateAsync(http);
        var result = await client.CallToolAsync("get_project_profile", new Dictionary<string, object?> { ["projectContext"] = "Docs" });
        result.IsError.Should().NotBe(true);
        result.StructuredContent!.Value.GetProperty("status").GetString().Should().Be("unknown");
        result.Content.OfType<TextContentBlock>().Should().ContainSingle().Which.Text.Should().Contain("unknown");
        var tool = (await client.ListToolsAsync()).Single(t => t.Name == "get_project_profile");
        JsonSchema.FromText(tool.ProtocolTool.OutputSchema!.Value.GetRawText())
            .Evaluate(result.StructuredContent.Value).IsValid.Should().BeTrue();
    }
}
