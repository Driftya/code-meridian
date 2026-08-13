using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using FluentAssertions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace CodeMeridian.McpServer.Tests;

public sealed class McpEndpointTests : IClassFixture<GraphQlWebApplicationFactory>
{
    private static readonly HashSet<string> ExpectedToolNames =
    [
        "query_codebase",
        "get_architectural_overview",
        "search_documentation",
        "find_tool_dependency_impact",
        "find_impact",
        "find_diagnostics",
        "find_diagnostics_for_node",
        "find_stale_knowledge",
        "knowledge_decay",
        "find_implementation_surface",
        "analyze_feature_implementation_path",
        "plan_edit_route",
        "replace_surface",
        "resolve_exact_symbol",
        "check_graph_freshness",
        "find_graph_drift",
        "plan_context_workflow",
        "execute_context_workflow",
        "find_config_definitions",
        "find_config_usage",
        "find_hotspots",
        "find_frontend_cascade_conflicts",
        "find_connection",
        "trace_endpoint",
        "find_unreferenced",
        "find_cross_project_dependencies",
        "find_coverage_gaps",
        "find_test_shield",
        "find_recently_changed",
        "find_large_nodes",
        "get_context_for_editing",
        "build_minimal_context",
        "find_god_classes",
        "find_downstream",
        "find_cycles",
        "architecture_drift_history",
        "find_architecture_violations",
        "find_smell_paths",
        "find_high_churn",
        "analyze_changed_subgraph",
        "get_pagerank",
        "get_betweenness",
        "find_bridges",
        "find_natural_modules",
        "suggest_extractions",
        "suggest_responsibility_slices",
        "find_similar_nodes",
        "hybrid_search",
        "find_implementation_patterns",
        "find_duplicate_candidates",
        "find_related_knowledge",
        "rebuild_keyword_graph",
        "classify_keywords",
        "ingest_code_node",
        "ingest_relationship",
        "ingest_document",
        "clear_project_knowledge",
        "clear_code_graph",
        "get_client_extension_contract",
        "list_client_extension_examples",
        "get_client_extension_example",
        "link_external_concept",
        "record_change_context",
        "get_change_context",
        "start_change_context_challenge",
        "answer_change_context_challenge",
        "record_change_context_challenge_note"
    ];

    private static readonly HashSet<string> MutatingToolNames =
    [
        "rebuild_keyword_graph",
        "classify_keywords",
        "ingest_code_node",
        "ingest_relationship",
        "ingest_document",
        "clear_project_knowledge",
        "clear_code_graph",
        "link_external_concept",
        "record_change_context",
        "start_change_context_challenge",
        "answer_change_context_challenge",
        "record_change_context_challenge_note"
    ];

    private static readonly HashSet<string> DestructiveToolNames =
    [
        "clear_project_knowledge",
        "clear_code_graph"
    ];

    private static readonly HashSet<string> IdempotentMutatingToolNames =
    [
        "record_change_context",
        "record_change_context_challenge_note"
    ];

    private static readonly HashSet<string> StructuredToolNames =
    [
        "check_graph_freshness",
        "find_impact",
        "find_test_shield",
        "build_minimal_context",
        "find_connection",
        "get_client_extension_contract",
        "list_client_extension_examples",
        "get_client_extension_example",
        "get_change_context",
        "start_change_context_challenge",
        "answer_change_context_challenge",
        "record_change_context_challenge_note"
    ];

    private readonly GraphQlWebApplicationFactory _factory;

    public McpEndpointTests(GraphQlWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task StreamableHttpEndpoint_ListsToolsWithObjectInputSchemas()
    {
        using var httpClient = _factory.CreateClient();
        await using var client = await McpTestClient.CreateAsync(httpClient);
        var tools = await client.ListToolsAsync();

        tools.Should().Contain(tool => tool.Name == "query_codebase");
        tools.Should().OnlyContain(tool =>
            tool.ProtocolTool.InputSchema.ValueKind == JsonValueKind.Object);

        tools.Single(tool => tool.Name == "query_codebase")
            .ProtocolTool.InputSchema.GetProperty("properties")
            .GetProperty("projectContext")
            .TryGetProperty("x-mcp-header", out _)
            .Should().BeFalse("optional arguments must not become required HTTP integrity mirrors");
    }

    [Fact]
    public async Task StreamableHttpEndpoint_AdvertisesReviewedToolInventoryAndAnnotations()
    {
        using var httpClient = _factory.CreateClient();
        await using var client = await McpTestClient.CreateAsync(httpClient);
        var tools = await client.ListToolsAsync();

        tools.Select(tool => tool.Name).Should().BeEquivalentTo(ExpectedToolNames);

        foreach (var tool in tools)
        {
            var protocolTool = tool.ProtocolTool;
            var annotations = protocolTool.Annotations;

            protocolTool.Title.Should().NotBeNullOrWhiteSpace($"{tool.Name} needs a human-readable title");
            annotations.Should().NotBeNull($"{tool.Name} needs reviewed behavior annotations");
            annotations!.ReadOnlyHint.Should().Be(
                !MutatingToolNames.Contains(tool.Name),
                $"{tool.Name} must advertise its actual mutation behavior");
            annotations.DestructiveHint.Should().Be(
                DestructiveToolNames.Contains(tool.Name),
                $"{tool.Name} must advertise its actual destructive behavior");
            annotations.IdempotentHint.Should().Be(
                !MutatingToolNames.Contains(tool.Name)
                || DestructiveToolNames.Contains(tool.Name)
                || IdempotentMutatingToolNames.Contains(tool.Name),
                $"{tool.Name} must advertise a conservative retry contract");
            annotations.OpenWorldHint.Should().BeFalse(
                $"{tool.Name} only operates on CodeMeridian-controlled state");

            if (StructuredToolNames.Contains(tool.Name))
                protocolTool.OutputSchema.Should().NotBeNull($"{tool.Name} advertises structured content");
            else
                protocolTool.OutputSchema.Should().BeNull($"{tool.Name} has not adopted structured content yet");
        }
    }

    [Fact]
    public async Task StreamableHttpEndpoint_ReturnsStructuredFactsAndCompatibleText()
    {
        using var httpClient = _factory.CreateClient();
        await using var client = await McpTestClient.CreateAsync(httpClient);

        var result = await client.CallToolAsync(
            "get_client_extension_contract",
            new Dictionary<string, object?>());

        result.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>()
            .Should().ContainSingle()
            .Which.Text.Should().StartWith("# Client Extension Contract");
        result.StructuredContent.Should().NotBeNull();
        result.StructuredContent!.Value.ValueKind.Should().Be(JsonValueKind.Object);
        result.StructuredContent.Value.GetProperty("graphQlEndpointPath").GetString().Should().Be("/graphql");
        result.StructuredContent.Value.GetProperty("version").GetString().Should().Be("v1");
    }

    [Fact]
    public async Task StreamableHttpEndpoint_AdvertisesPrivateToolCatalogCachingHints()
    {
        using var httpClient = _factory.CreateClient();
        await using var client = await McpTestClient.CreateAsync(httpClient);

        var result = await client.ListToolsAsync(new ListToolsRequestParams());

        result.TimeToLive.Should().Be(TimeSpan.FromMinutes(5));
        result.CacheScope.Should().Be(CacheScope.Private);
        result.Tools.Should().HaveCount(ExpectedToolNames.Count);
    }

    [Fact]
    public async Task StreamableHttpEndpoint_EmitsBoundedToolTelemetry()
    {
        var activityStopped = new TaskCompletionSource<Activity>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "CodeMeridian.McpServer",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity =>
            {
                if (activity.OperationName == "mcp.tools.call"
                    && activity.GetTagItem("mcp.tool.name")?.ToString() == "get_client_extension_contract")
                {
                    activityStopped.TrySetResult(activity);
                }
            }
        };
        ActivitySource.AddActivityListener(activityListener);

        var metricObserved = new TaskCompletionSource<IReadOnlyDictionary<string, object?>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == "CodeMeridian.McpServer")
                listener.EnableMeasurementEvents(instrument);
        };
        meterListener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
        {
            if (instrument.Name != "codemeridian.mcp.tool.calls" || measurement != 1)
                return;

            var capturedTags = tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value);
            if (capturedTags.GetValueOrDefault("mcp.tool.category")?.ToString() == "query"
                && capturedTags.GetValueOrDefault("mcp.tool.outcome")?.ToString() == "success")
            {
                metricObserved.TrySetResult(capturedTags);
            }
        });
        meterListener.Start();

        using var httpClient = _factory.CreateClient();
        await using var client = await McpTestClient.CreateAsync(httpClient);
        await client.CallToolAsync(
            "get_client_extension_contract",
            new Dictionary<string, object?>());

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var activity = await activityStopped.Task.WaitAsync(timeout.Token);
        var metricTags = await metricObserved.Task.WaitAsync(timeout.Token);

        activity.GetTagItem("mcp.tool.category").Should().Be("query");
        activity.GetTagItem("mcp.tool.outcome").Should().Be("success");
        activity.Status.Should().Be(ActivityStatusCode.Ok);
        metricTags["mcp.tool.category"].Should().Be("query");
        metricTags["mcp.tool.outcome"].Should().Be("success");
    }

}
