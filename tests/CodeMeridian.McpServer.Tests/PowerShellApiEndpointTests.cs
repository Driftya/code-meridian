using System.Net;
using System.Net.Http.Json;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.Projects;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;

namespace CodeMeridian.McpServer.Tests;

public sealed class PowerShellApiEndpointTests
{
    [Fact]
    public async Task PublicationRequiresAuthenticationValidatesAndForwardsGenerationConflicts()
    {
        var repository = Substitute.For<IPowerShellGraphRepository>();
        await using var factory = new GraphQlWebApplicationFactory().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IPowerShellGraphRepository>(); services.AddSingleton(repository);
        }));
        using var client = factory.CreateClient();
        const string endpoint = "/api/v1/knowledge/powershell";
        var hash = "sha256:" + new string('a', 64);
        var snapshot = new PowerShellGraphSnapshot("Test", 3, hash, new(hash, hash), []);
        (await client.PostAsJsonAsync(endpoint, snapshot)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        client.DefaultRequestHeaders.Authorization = new("Bearer", GraphQlWebApplicationFactory.ApiKey);
        (await client.PostAsJsonAsync(endpoint, snapshot with { Generation = 0 })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PostAsJsonAsync(endpoint, snapshot)).StatusCode.Should().Be(HttpStatusCode.OK);
        await repository.Received(1).PublishPowerShellGraphAsync(Arg.Is<PowerShellGraphSnapshot>(value => value.Generation == 3), Arg.Any<CancellationToken>());
        repository.PublishPowerShellGraphAsync(Arg.Any<PowerShellGraphSnapshot>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new ProjectProfileConflictException("stale")));
        (await client.PostAsJsonAsync(endpoint, snapshot)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await client.PostAsync(endpoint, new StringContent("{}"))).StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
    }
}
