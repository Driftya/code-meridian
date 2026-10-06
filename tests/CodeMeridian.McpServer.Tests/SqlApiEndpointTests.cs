using System.Net;
using System.Net.Http.Json;
using CodeMeridian.Core.CodeGraph;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;

namespace CodeMeridian.McpServer.Tests;

public sealed class SqlApiEndpointTests
{
    [Fact]
    public async Task SqlEndpoint_RequiresAuthenticationValidatesAndPublishesStringEnumWorkerPayload()
    {
        var repository = Substitute.For<ISqlGraphRepository>();
        await using var factory = new GraphQlWebApplicationFactory().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<ISqlGraphRepository>();
            services.AddSingleton(repository);
        }));
        using var client = factory.CreateClient();
        const string endpoint = "/api/v1/knowledge/sql";
        var response = await client.PostAsJsonAsync(endpoint, new { contractVersion = "1", projectContext = "SqlTest", files = Array.Empty<object>() });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        client.DefaultRequestHeaders.Authorization = new("Bearer", GraphQlWebApplicationFactory.ApiKey);
        response = await client.PostAsJsonAsync(endpoint, new
        {
            contractVersion = "1", projectContext = "SqlTest", files = new[] { new
            {
                path = "a.sql", status = "complete", diagnostics = Array.Empty<string>(), edges = Array.Empty<object>(),
                nodes = new[] { new { id = "SqlTest::Sql::File", name = "a.sql", type = "File", projectContext = "SqlTest", filePath = "a.sql" } }
            } }
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await repository.Received(1).PublishSqlGraphAsync(Arg.Is<SqlGraphSnapshot>(s => s.Files[0].Nodes[0].Type == CodeNodeType.File), Arg.Any<CancellationToken>());
        response = await client.PostAsJsonAsync(endpoint, new { contractVersion = "unsupported", projectContext = "SqlTest", files = Array.Empty<object>() });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await repository.Received(1).PublishSqlGraphAsync(Arg.Any<SqlGraphSnapshot>(), Arg.Any<CancellationToken>());
    }
}
