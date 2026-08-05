using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
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
        queryService.CheckGraphFreshnessResultAsync(null, null, 25, Arg.Any<CancellationToken>())
            .Returns(CreateFreshness());
        queryService.FindImpactResultAsync("source", 5, false, Arg.Any<CancellationToken>())
            .Returns(CreateImpact());
        queryService.FindTestShieldResultAsync("source", null, 2, 20, Arg.Any<CancellationToken>())
            .Returns(CreateTestShield());
        queryService.BuildMinimalContextResultAsync(
                "source", null, 3000, true, true, false, false, ContextDetailLevel.Compact,
                Arg.Any<CancellationToken>())
            .Returns(CreateMinimalContext());

        using var factory = WithQueryService(queryService);
        using var httpClient = factory.CreateClient();
        await using var client = await McpTestClient.CreateAsync(httpClient);
        var tools = await client.ListToolsAsync();
        var calls = new[]
        {
            new StructuredCall("check_graph_freshness", []),
            new StructuredCall("find_impact", new Dictionary<string, object?>
            {
                ["nodeId"] = "source"
            }),
            new StructuredCall("find_test_shield", new Dictionary<string, object?>
            {
                ["nodeId"] = "source"
            }),
            new StructuredCall("build_minimal_context", new Dictionary<string, object?>
            {
                ["target"] = "source"
            }),
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

    [Fact]
    public async Task MinimalContextResult_KeepsSnippetBodiesOutOfStructuredContent()
    {
        const string snippetBody = "private api key shaped source body";
        var queryService = Substitute.For<ICodebaseQueryService>();
        var minimalContext = CreateMinimalContext() with
        {
            IncludeSourceSnippets = true,
            Budget = CreateMinimalContext().Budget with
            {
                SourceSnippetBudgetTokens = 100,
                SourceSnippetEstimatedTokens = 7
            },
            Snippets =
            [
                new MinimalContextSnippetResult(
                    Node(), 7, false, snippetBody)
            ]
        };
        queryService.BuildMinimalContextResultAsync(
                "source", null, 3000, true, true, false, false, ContextDetailLevel.Compact,
                Arg.Any<CancellationToken>())
            .Returns(minimalContext);

        using var factory = WithQueryService(queryService);
        using var httpClient = factory.CreateClient();
        await using var client = await McpTestClient.CreateAsync(httpClient);
        var result = await client.CallToolAsync(
            "build_minimal_context",
            new Dictionary<string, object?> { ["target"] = "source" });
        var structuredJson = result.StructuredContent!.Value.GetRawText();

        structuredJson.Should().NotContain(snippetBody);
        structuredJson.Should().NotContain("markdownText");
        structuredJson.Should().NotContain("\"sourceSnippet\":");
        structuredJson.Should().NotContain("\"properties\":");
        result.Content.OfType<TextContentBlock>().Should().ContainSingle()
            .Which.Text.Should().Contain(snippetBody);
    }

    [Fact]
    public async Task AdvertisedStructuredSchemas_MatchBoundedFingerprintSnapshot()
    {
        using var httpClient = _factory.CreateClient();
        await using var client = await McpTestClient.CreateAsync(httpClient);
        var schemas = (await client.ListToolsAsync())
            .Where(tool => tool.ProtocolTool.OutputSchema is not null)
            .OrderBy(tool => tool.Name, StringComparer.Ordinal)
            .Select(tool =>
            {
                var schema = tool.ProtocolTool.OutputSchema!.Value;
                var required = schema.TryGetProperty("required", out var requiredElement)
                    ? requiredElement.EnumerateArray().Select(value => value.GetString()).ToArray()
                    : [];
                var properties = schema.TryGetProperty("properties", out var propertiesElement)
                    ? propertiesElement.EnumerateObject().Select(property => property.Name).ToArray()
                    : [];

                return new
                {
                    name = tool.Name,
                    required,
                    properties,
                    sha256 = Convert.ToHexStringLower(
                        SHA256.HashData(Encoding.UTF8.GetBytes(schema.GetRawText())))
                };
            })
            .ToArray();
        var actual = JsonSerializer.SerializeToNode(schemas);
        var snapshotPath = Path.Combine(
            AppContext.BaseDirectory, "Snapshots", "mcp-structured-schema-baseline.json");
        var expected = JsonNode.Parse(await File.ReadAllTextAsync(snapshotPath));

        JsonNode.DeepEquals(actual, expected).Should().BeTrue(
            "structured schema changes must be reviewed intentionally; " +
            $"actual: {actual?.ToJsonString(new JsonSerializerOptions { WriteIndented = true })}");
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

    private static GraphFreshnessResult CreateFreshness() =>
        new(
            "1.0",
            "Example",
            null,
            true,
            1,
            0,
            0,
            Relationship(),
            [new GraphFreshnessFindingResult(Node(), "High", "checksum indexed", "present", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, "fresh")],
            false,
            null);

    private static ImpactAnalysisResult CreateImpact() =>
        new(
            "1.0",
            "source",
            5,
            true,
            false,
            "NotEvaluated",
            Relationship(),
            [new ImpactFindingResult(Node(), 1, "Unclassified", string.Empty, "—", "direct-class", [])],
            false);

    private static TestShieldResult CreateTestShield() =>
        new(
            "1.0", "source", "Example", 2, true, Node(), Relationship(),
            [], [], [], [], [], null, false);

    private static MinimalContextResult CreateMinimalContext() =>
        new(
            "1.0", "source", null, true, Node(), ContextDetailLevel.Compact.ToString(),
            true, true, false, false, Relationship(),
            new MinimalContextBudgetResult(3000, 400, 0, 0, true, MaximumStructuredPayloadBytes, "Low", "Small model", "Low", "bounded"),
            [], [], [], [], [], [], [], null, ["src/Source.cs"], [], [], [],
            new MinimalContextTruncationResult(false, false, false, false, false, false, false, false, false));

    private static GraphNodeResult Node() =>
        new("source", "Source", "Method", "Example", "src/Source.cs", 10, 12, "Example", "Bounded summary");

    private static RelationshipCompletenessResult Relationship() =>
        new("High", "complete", DateTimeOffset.UnixEpoch, null, 0, 0, 0, 0, 0, []);

    private sealed record StructuredCall(
        string Name,
        Dictionary<string, object?> Arguments);
}
