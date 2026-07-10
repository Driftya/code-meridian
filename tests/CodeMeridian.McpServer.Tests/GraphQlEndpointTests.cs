using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CodeMeridian.Application.GraphQueries;
using CodeMeridian.Core.GraphQueries;
using CodeMeridian.Infrastructure;
using CodeMeridian.McpServer.Keywording;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NSubstitute;

namespace CodeMeridian.McpServer.Tests;

public sealed class GraphQlEndpointTests : IClassFixture<GraphQlWebApplicationFactory>
{
    private readonly GraphQlWebApplicationFactory _factory;

    public GraphQlEndpointTests(GraphQlWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GraphQlEndpoint_WithoutApiKey_ReturnsUnauthorized()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/graphql", new
        {
            query = "{ labels }"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GraphQlEndpoint_WithApiKey_ReturnsLabelsAndRelationshipTypes()
    {
        _factory.QueryService.ListLabelsAsync(Arg.Any<CancellationToken>())
            .Returns(["CodeNode", "Keyword"]);
        _factory.QueryService.ListRelationshipTypesAsync(Arg.Any<CancellationToken>())
            .Returns(["Calls", "HAS_KEYWORD"]);

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-CodeMeridian-ApiKey", GraphQlWebApplicationFactory.ApiKey);

        var response = await client.PostAsJsonAsync("/graphql", new
        {
            query = "{ labels relationshipTypes }"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        payload.GetProperty("data").GetProperty("labels")[0].GetString().Should().Be("CodeNode");
        payload.GetProperty("data").GetProperty("relationshipTypes")[1].GetString().Should().Be("HAS_KEYWORD");
    }

    [Fact]
    public async Task OpenApiEndpoint_WithoutApiKey_DocumentsRestApi()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var paths = payload.GetProperty("paths");
        paths.TryGetProperty("/api/v1/status/version", out _).Should().BeTrue();
        paths.TryGetProperty("/api/v1/knowledge/nodes", out _).Should().BeTrue();
    }

    [Fact]
    public async Task SwaggerUi_WithoutApiKey_ReturnsHtml()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/swagger/index.html");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("CodeMeridian API");
    }

    [Fact]
    public async Task GraphQlEndpoint_NodeQuery_ReturnsPropertiesAndRelationships()
    {
        var expectedFromNodeIds = new[] { "keyword:1" };

        _factory.QueryService.GetNodeAsync("keyword:1", Arg.Any<CancellationToken>())
            .Returns(new GraphNode
            {
                Id = "keyword:1",
                Labels = ["Keyword"],
                PrimaryLabel = "Keyword",
                Name = "GraphQL",
                Properties =
                [
                    new GraphProperty
                    {
                        Key = "normalizedValue",
                        Value = "graphql",
                        ValueKind = GraphPropertyValueKind.String
                    }
                ]
            });
        _factory.QueryService.QueryRelationshipsAsync(
                Arg.Is<GraphRelationshipFilter>(filter => filter.FromNodeIds.SequenceEqual(expectedFromNodeIds)),
                Arg.Any<GraphSort?>(),
                0,
                5,
                Arg.Any<CancellationToken>())
            .Returns(
            [
                new GraphRelationship
                {
                    Id = "rel-1",
                    Type = "RELATED_TO",
                    FromNodeId = "keyword:1",
                    ToNodeId = "keyword:2"
                }
            ]);

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-CodeMeridian-ApiKey", GraphQlWebApplicationFactory.ApiKey);

        var response = await client.PostAsJsonAsync("/graphql", new
        {
            query = """
                {
                  node(id: "keyword:1") {
                    id
                    labels
                    properties {
                      key
                      value
                      valueKind
                    }
                    outgoingRelationships(limit: 5) {
                      type
                      toNodeId
                    }
                  }
                }
                """
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var node = payload.GetProperty("data").GetProperty("node");
        node.GetProperty("labels")[0].GetString().Should().Be("Keyword");
        node.GetProperty("properties")[0].GetProperty("value").GetString().Should().Be("graphql");
        node.GetProperty("outgoingRelationships")[0].GetProperty("toNodeId").GetString().Should().Be("keyword:2");
    }
}

public sealed class GraphQlWebApplicationFactory : WebApplicationFactory<Program>
{
    internal const string ApiKey = "test-graph-key";

    internal IGraphQueryService QueryService { get; } = Substitute.For<IGraphQueryService>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Environment.SetEnvironmentVariable("CodeMeridian_Auth_ApiKey", ApiKey);

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IGraphQueryService>();
            services.AddSingleton(QueryService);
            services.RemoveAll<IHostedService>();
        });
    }
}
