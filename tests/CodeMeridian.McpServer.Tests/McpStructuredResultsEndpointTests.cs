using System.Text;
using System.Text.Json;
using CodeMeridian.Application.Services;
using FluentAssertions;
using Json.Schema;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModelContextProtocol.Protocol;
using NSubstitute;

namespace CodeMeridian.McpServer.Tests;

public sealed class McpStructuredResultsEndpointTests : IClassFixture<GraphQlWebApplicationFactory>
{
    private const int MaximumStructuredPayloadBytes = 128 * 1024;
    private readonly GraphQlWebApplicationFactory _factory;

    public McpStructuredResultsEndpointTests(GraphQlWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task AdvertisedSchemas_ValidateEveryImplementedStructuredResult()
    {
        var queryService = Substitute.For<ICodebaseQueryService>();
        queryService.FindConnectionResultAsync("source", "target", Arg.Any<CancellationToken>())
            .Returns(CreateConnection());

        using var factory = WithQueryService(queryService);
        using var httpClient = factory.CreateClient();
        await using var client = await McpTestClient.CreateAsync(httpClient);
        var tools = await client.ListToolsAsync();
        var calls = new[]
        {
            new StructuredCall("find_connection", new Dictionary<string, object?>
            {
                ["fromId"] = "source",
                ["toId"] = "target"
            }),
            new StructuredCall("get_client_extension_contract", []),
            new StructuredCall("list_client_extension_examples", []),
            new StructuredCall("get_client_extension_example", new Dictionary<string, object?>
            {
                ["exampleId"] = "keyword-search"
            })
        };

        foreach (var call in calls)
        {
            var tool = tools.Single(tool => tool.Name == call.Name);
            var outputSchema = tool.ProtocolTool.OutputSchema;
            outputSchema.Should().NotBeNull($"{call.Name} advertises structured content");
            var result = await client.CallToolAsync(call.Name, call.Arguments);
            var structuredContent = result.StructuredContent;
            structuredContent.Should().NotBeNull($"{call.Name} returns structured content");
            var evaluation = JsonSchema.FromText(outputSchema!.Value.GetRawText())
                .Evaluate(structuredContent!.Value);

            evaluation.IsValid.Should().BeTrue(
                $"{call.Name} must satisfy its advertised output schema. " +
                $"Schema: {outputSchema.Value.GetRawText()}. " +
                $"Instance: {structuredContent.Value.GetRawText()}. " +
                $"Evaluation: {JsonSerializer.Serialize(evaluation)}");
            Encoding.UTF8.GetByteCount(structuredContent.Value.GetRawText())
                .Should().BeLessThan(MaximumStructuredPayloadBytes);
        }
    }

    [Fact]
    public async Task ConnectionSchema_RejectsAnInvalidContractShape()
    {
        var queryService = Substitute.For<ICodebaseQueryService>();
        queryService.FindConnectionResultAsync("source", "target", Arg.Any<CancellationToken>())
            .Returns(CreateConnection());

        using var factory = WithQueryService(queryService);
        using var httpClient = factory.CreateClient();
        await using var client = await McpTestClient.CreateAsync(httpClient);
        var tool = (await client.ListToolsAsync()).Single(tool => tool.Name == "find_connection");
        var schema = JsonSchema.FromText(tool.ProtocolTool.OutputSchema!.Value.GetRawText());
        using var invalidPayload = JsonDocument.Parse("""{"contractVersion":1,"nodes":null}""");

        schema.Evaluate(invalidPayload.RootElement).IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task ConnectionResult_UsesEmptyCollectionsAndDoesNotLeakSourceBodies()
    {
        var queryService = Substitute.For<ICodebaseQueryService>();
        queryService.FindConnectionResultAsync("a", "b", Arg.Any<CancellationToken>())
            .Returns(new ConnectionAnalysisResult(
                "1.0", "a", "b", 10, false, 0, false, [], [], []));

        using var factory = WithQueryService(queryService);
        using var httpClient = factory.CreateClient();
        await using var client = await McpTestClient.CreateAsync(httpClient);
        var result = await client.CallToolAsync(
            "find_connection",
            new Dictionary<string, object?> { ["fromId"] = "a", ["toId"] = "b" });
        var facts = result.StructuredContent!.Value;

        facts.GetProperty("pathFound").GetBoolean().Should().BeFalse();
        facts.GetProperty("nodes").GetArrayLength().Should().Be(0);
        facts.GetProperty("edges").GetArrayLength().Should().Be(0);
        facts.GetProperty("frontendSignals").GetArrayLength().Should().Be(0);
        facts.GetRawText().Should().NotContain("sourceSnippet");
        facts.GetRawText().Should().NotContain("properties");
        result.Content.OfType<TextContentBlock>().Should().ContainSingle()
            .Which.Text.Should().Contain("No path found");
    }

    private WebApplicationFactory<Program> WithQueryService(ICodebaseQueryService queryService) =>
        _factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ICodebaseQueryService>();
                services.AddSingleton(queryService);
            }));

    private static ConnectionAnalysisResult CreateConnection() =>
        new(
            "1.0",
            "source",
            "target",
            10,
            true,
            1,
            false,
            [new ConnectionNodeResult(0, "source", "Source", "Class", "Example", "src/Source.cs", 10, "Example"),
             new ConnectionNodeResult(1, "target", "Target", "Method", "Example", "src/Target.cs", 20, "Example")],
            [new ConnectionEdgeResult(0, "source", "target", "Calls")],
            []);

    private sealed record StructuredCall(
        string Name,
        Dictionary<string, object?> Arguments);
}
