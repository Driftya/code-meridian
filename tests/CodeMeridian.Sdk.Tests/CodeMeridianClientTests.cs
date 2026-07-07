using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CodeMeridian.Sdk;
using CodeMeridian.Sdk.Versioning;
using FluentAssertions;

namespace CodeMeridian.Sdk.Tests;

public sealed class CodeMeridianClientTests
{
    [Fact]
    public async Task IngestDocumentAsync_SendsRelatedNodeIdsCsv()
    {
        var handler = new CapturingHandler();
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost")
        };
        var sut = new CodeMeridianClient(client);

        await sut.IngestDocumentAsync(
            content: "ADR-004 references PaymentGateway.ChargeAsync",
            source: "docs/adr-004.md",
            projectContext: "Shop",
            id: "doc-1",
            relatedNodeIdsCsv: "Method:Shop.Payments.PaymentGateway.ChargeAsync");

        handler.Request.Should().NotBeNull();
        handler.Request!.Method.Should().Be(HttpMethod.Post);
        handler.Request.RequestUri!.AbsolutePath.Should().Be("/api/v1/knowledge/documents");

        var body = await handler.ReadBodyAsync();
        body.GetProperty("relatedNodeIdsCsv").GetString().Should().Be("Method:Shop.Payments.PaymentGateway.ChargeAsync");
        body.GetProperty("content").GetString().Should().Contain("PaymentGateway.ChargeAsync");
    }

    [Fact]
    public async Task DeleteProjectFileAsync_SendsEncodedFileDeleteRequest()
    {
        var handler = new CapturingHandler();
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost")
        };
        var sut = new CodeMeridianClient(client);

        await sut.DeleteProjectFileAsync("My Project", "src/App/Order Service.cs");

        handler.Request.Should().NotBeNull();
        handler.Request!.Method.Should().Be(HttpMethod.Delete);
        handler.Request.RequestUri!.AbsolutePath.Should().Be("/api/v1/knowledge/project/My%20Project/files/src%2FApp%2FOrder%20Service.cs");
    }

    [Fact]
    public async Task IngestCodeNodeAsync_SendsSourceHash()
    {
        var handler = new CapturingHandler();
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost")
        };
        var sut = new CodeMeridianClient(client);

        await sut.IngestCodeNodeAsync(
            id: "Method:Shop.OrderService.PlaceOrder",
            name: "PlaceOrder",
            type: "Method",
            sourceHash: "abc123",
            projectContext: "Shop");

        handler.Request.Should().NotBeNull();
        handler.Request!.Method.Should().Be(HttpMethod.Post);
        handler.Request.RequestUri!.AbsolutePath.Should().Be("/api/v1/knowledge/nodes");

        var body = await handler.ReadBodyAsync();
        body.GetProperty("sourceHash").GetString().Should().Be("abc123");
    }

    [Fact]
    public async Task IngestRelationshipAsync_SendsEdgeMetadata()
    {
        var handler = new CapturingHandler();
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost")
        };
        var sut = new CodeMeridianClient(client);

        await sut.IngestRelationshipAsync(
            sourceId: "frontend",
            targetId: "endpoint",
            relationshipType: "Calls",
            isAsync: true,
            callSite: "src/app.ts:42",
            paramCount: 2,
            confidence: 0.95);

        var body = await handler.ReadBodyAsync();
        body.GetProperty("isAsync").GetBoolean().Should().BeTrue();
        body.GetProperty("callSite").GetString().Should().Be("src/app.ts:42");
        body.GetProperty("paramCount").GetInt32().Should().Be(2);
        body.GetProperty("confidence").GetDouble().Should().Be(0.95);
    }

    [Fact]
    public async Task IngestCodeNodesAsync_SendsBulkNodePayload()
    {
        var handler = new CapturingHandler();
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost")
        };
        var sut = new CodeMeridianClient(client);

        await sut.IngestCodeNodesAsync(
        [
            new CodeNodeIngestRequest("node-1", "Node1", "File", FilePath: "src/Node1.cs", ProjectContext: "Shop"),
            new CodeNodeIngestRequest("node-2", "Node2", "File", FilePath: "src/Node2.cs", ProjectContext: "Shop")
        ]);

        handler.Request.Should().NotBeNull();
        handler.Request!.RequestUri!.AbsolutePath.Should().Be("/api/v1/knowledge/nodes/bulk");
        var body = await handler.ReadBodyAsync();
        body.ValueKind.Should().Be(JsonValueKind.Array);
        body.GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task IngestRelationshipsAsync_SendsBulkEdgePayload()
    {
        var handler = new CapturingHandler();
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost")
        };
        var sut = new CodeMeridianClient(client);

        await sut.IngestRelationshipsAsync(
        [
            new CodeEdgeIngestRequest("frontend", "endpoint", "Calls"),
            new CodeEdgeIngestRequest("endpoint", "handler", "Uses")
        ]);

        handler.Request.Should().NotBeNull();
        handler.Request!.RequestUri!.AbsolutePath.Should().Be("/api/v1/knowledge/nodes/edges/bulk");
        var body = await handler.ReadBodyAsync();
        body.ValueKind.Should().Be(JsonValueKind.Array);
        body.GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task IngestDocumentsAsync_SendsBulkDocumentPayload()
    {
        var handler = new CapturingHandler();
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost")
        };
        var sut = new CodeMeridianClient(client);

        await sut.IngestDocumentsAsync(
        [
            new KnowledgeDocumentIngestRequest("one", "doc-1", "docs/one.md", "Shop"),
            new KnowledgeDocumentIngestRequest("two", "doc-2", "docs/two.md", "Shop")
        ]);

        handler.Request.Should().NotBeNull();
        handler.Request!.RequestUri!.AbsolutePath.Should().Be("/api/v1/knowledge/documents/bulk");
        var body = await handler.ReadBodyAsync();
        body.ValueKind.Should().Be(JsonValueKind.Array);
        body.GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task IngestCodeNodesAsync_ThrowsOnBulkFailure()
    {
        var handler = new CapturingHandler(HttpStatusCode.InternalServerError);
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost")
        };
        var sut = new CodeMeridianClient(client);

        var act = async () => await sut.IngestCodeNodesAsync(
        [
            new CodeNodeIngestRequest("node-1", "Node1", "File", FilePath: "src/Node1.cs", ProjectContext: "Shop")
        ]);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task IngestRelationshipsAsync_ThrowsOnBulkFailure()
    {
        var handler = new CapturingHandler(HttpStatusCode.InternalServerError);
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost")
        };
        var sut = new CodeMeridianClient(client);

        var act = async () => await sut.IngestRelationshipsAsync(
        [
            new CodeEdgeIngestRequest("frontend", "endpoint", "Calls")
        ]);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task IngestDocumentsAsync_ThrowsOnBulkFailure()
    {
        var handler = new CapturingHandler(HttpStatusCode.InternalServerError);
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost")
        };
        var sut = new CodeMeridianClient(client);

        var act = async () => await sut.IngestDocumentsAsync(
        [
            new KnowledgeDocumentIngestRequest("one", "doc-1", "docs/one.md", "Shop")
        ]);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task GetDoctorStatusAsync_SendsProjectContextQuery()
    {
        var handler = new CapturingHandler();
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost")
        };
        var sut = new CodeMeridianClient(client);

        var status = await sut.GetDoctorStatusAsync("My Project");

        handler.Request.Should().NotBeNull();
        handler.Request!.Method.Should().Be(HttpMethod.Get);
        handler.Request.RequestUri!.AbsolutePath.Should().Be("/api/v1/status/doctor");
        handler.Request.RequestUri.Query.Should().Contain("projectContext=My%20Project");
        status.Should().NotBeNull();
        status!.GraphDriftReport.Should().Be("Graph drift: low");
    }

    [Fact]
    public async Task GetArchitectureReportAsync_SendsProjectContextQuery()
    {
        var handler = new CapturingHandler();
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost")
        };
        var sut = new CodeMeridianClient(client);

        var report = await sut.GetArchitectureReportAsync("My Project");

        handler.Request.Should().NotBeNull();
        handler.Request!.Method.Should().Be(HttpMethod.Get);
        handler.Request.RequestUri!.AbsolutePath.Should().Be("/api/v1/status/report");
        handler.Request.RequestUri.Query.Should().Contain("projectContext=My%20Project");
        report.Should().Contain("# Architecture Weather Report");
    }

    [Fact]
    public async Task GetEndpointTraceAsync_SendsRouteAndDetailLevelQuery()
    {
        var handler = new CapturingHandler();
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost")
        };
        var sut = new CodeMeridianClient(client);

        var report = await sut.GetEndpointTraceAsync("POST /api/orders", "My Project", "Full");

        handler.Request.Should().NotBeNull();
        handler.Request!.Method.Should().Be(HttpMethod.Get);
        handler.Request.RequestUri!.AbsolutePath.Should().Be("/api/v1/status/trace-endpoint");
        handler.Request.RequestUri.Query.Should().Contain("route=POST%20%2Fapi%2Forders");
        handler.Request.RequestUri.Query.Should().Contain("projectContext=My%20Project");
        handler.Request.RequestUri.Query.Should().Contain("detailLevel=Full");
        report.Should().Contain("## Endpoint Trace");
    }

    [Fact]
    public async Task BuildPrContextReportAsync_SendsRequestBody()
    {
        var handler = new CapturingHandler();
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost")
        };
        var sut = new CodeMeridianClient(client);

        var report = await sut.BuildPrContextReportAsync(new PrContextReportRequest(
            ["src/Subscriptions/SubscriptionService.cs"],
            "My Project",
            "origin/main",
            "HEAD",
            IncludeDocs: true));

        handler.Request.Should().NotBeNull();
        handler.Request!.Method.Should().Be(HttpMethod.Post);
        handler.Request.RequestUri!.AbsolutePath.Should().Be("/api/v1/status/report/pr-context");
        var body = await handler.ReadBodyAsync();
        body.GetProperty("projectContext").GetString().Should().Be("My Project");
        body.GetProperty("baseRef").GetString().Should().Be("origin/main");
        body.GetProperty("changedFiles")[0].GetString().Should().Be("src/Subscriptions/SubscriptionService.cs");
        report.Should().NotBeNull();
        report!.RelatedDocuments.Should().ContainSingle();
    }

    [Fact]
    public async Task GetVersionAsync_SendsVersionRequest()
    {
        var handler = new CapturingHandler();
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost")
        };
        var sut = new CodeMeridianClient(client);

        var version = await sut.GetVersionAsync();

        handler.Request.Should().NotBeNull();
        handler.Request!.Method.Should().Be(HttpMethod.Get);
        handler.Request.RequestUri!.AbsolutePath.Should().Be("/api/v1/status/version");
        version.Should().BeEquivalentTo(new CodeMeridianComponentVersion("CodeMeridian.McpServer", "1.2.3", 1, 2));
    }

    [Fact]
    public async Task StartRebuildKeywordGraphAsync_SendsProjectContextBody()
    {
        var handler = new CapturingHandler();
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost")
        };
        var sut = new CodeMeridianClient(client);

        var result = await sut.StartRebuildKeywordGraphAsync("CodeMeridian", leaseTtlSeconds: 900);

        handler.Request.Should().NotBeNull();
        handler.Request!.Method.Should().Be(HttpMethod.Post);
        handler.Request.RequestUri!.AbsolutePath.Should().Be("/api/v1/knowledge/keywords/rebuild");

        var body = await handler.ReadBodyAsync();
        body.GetProperty("projectContext").GetString().Should().Be("CodeMeridian");
        body.GetProperty("leaseTtlSeconds").GetInt32().Should().Be(900);
        result.Should().NotBeNull();
        result!.Accepted.Should().BeTrue();
    }

    [Fact]
    public async Task StartClassifyKeywordsAsync_SendsProjectContextBody()
    {
        var handler = new CapturingHandler();
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost")
        };
        var sut = new CodeMeridianClient(client);

        var result = await sut.StartClassifyKeywordsAsync("CodeMeridian", leaseTtlSeconds: 600);

        handler.Request.Should().NotBeNull();
        handler.Request!.Method.Should().Be(HttpMethod.Post);
        handler.Request.RequestUri!.AbsolutePath.Should().Be("/api/v1/knowledge/keywords/classify");

        var body = await handler.ReadBodyAsync();
        body.GetProperty("projectContext").GetString().Should().Be("CodeMeridian");
        body.GetProperty("leaseTtlSeconds").GetInt32().Should().Be(600);
        result.Should().NotBeNull();
        result!.Accepted.Should().BeTrue();
    }

    [Fact]
    public async Task GetKeywordGraphJobStatusAsync_SendsJobRequest()
    {
        var handler = new CapturingHandler();
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost")
        };
        var sut = new CodeMeridianClient(client);
        var jobId = Guid.Parse("11111111-2222-3333-4444-555555555555");

        var status = await sut.GetKeywordGraphJobStatusAsync(jobId);

        handler.Request.Should().NotBeNull();
        handler.Request!.Method.Should().Be(HttpMethod.Get);
        handler.Request.RequestUri!.AbsolutePath.Should().Be($"/api/v1/knowledge/keywords/jobs/{jobId:D}");
        status.Should().NotBeNull();
        status!.JobId.Should().Be(jobId);
    }

        private sealed class CapturingHandler(HttpStatusCode statusCode = HttpStatusCode.Created) : HttpMessageHandler
        {
            public HttpRequestMessage? Request { get; private set; }
            private string? _body;

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Request = request;
                _body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

                return new HttpResponseMessage(HttpStatusCode.Created)
                {
                    StatusCode = statusCode,
                    Content = request.RequestUri!.AbsolutePath == "/api/v1/status/version"
                        ? JsonContent.Create(new CodeMeridianComponentVersion("CodeMeridian.McpServer", "1.2.3", 1, 2))
                        : request.RequestUri!.AbsolutePath == "/api/v1/status/report"
                            ? new StringContent("# Architecture Weather Report")
                        : request.RequestUri!.AbsolutePath == "/api/v1/status/trace-endpoint"
                            ? new StringContent("## Endpoint Trace")
                        : request.RequestUri!.AbsolutePath == "/api/v1/status/report/pr-context"
                            ? JsonContent.Create(new PrContextReportResponse(
                                "My Project",
                                "origin/main",
                                "HEAD",
                                ["src/Subscriptions/SubscriptionService.cs"],
                                [new PrContextNodeSummaryResponse("node-1", "SubscriptionService.SyncAsync", "Method", "src/Subscriptions/SubscriptionService.cs", "My Project", 12, 20)],
                                [new PrContextImpactSummaryResponse(
                                    new PrContextNodeSummaryResponse("node-2", "SubscriptionController", "Class", "src/Api/SubscriptionController.cs", "My Project", 4, 40),
                                    1,
                                    1)],
                                [],
                                [],
                                [new PrContextRelatedDocumentResponse("doc-1", "docs/features/subscriptions.md", "High", 9.5d, ["subscription", "badge"])],
                                ["Review the subscription flow and related docs."]))
                        : request.RequestUri!.AbsolutePath.StartsWith("/api/v1/knowledge/keywords/jobs/", StringComparison.Ordinal)
                            ? JsonContent.Create(new KeywordGraphJobStatusResponse(
                                Guid.Parse("11111111-2222-3333-4444-555555555555"),
                                "rebuild",
                                "CodeMeridian",
                                "Completed",
                                DateTimeOffset.Parse("2026-06-17T10:00:00Z"),
                                DateTimeOffset.Parse("2026-06-17T10:30:00Z"),
                                DateTimeOffset.Parse("2026-06-17T10:05:00Z"),
                                "done",
                                null))
                            : request.RequestUri!.AbsolutePath.StartsWith("/api/v1/knowledge/keywords/", StringComparison.Ordinal)
                                ? JsonContent.Create(new KeywordGraphJobSubmissionResponse(
                                    true,
                                    "Started",
                                    new KeywordGraphJobStatusResponse(
                                        Guid.Parse("11111111-2222-3333-4444-555555555555"),
                                        "rebuild",
                                        "CodeMeridian",
                                        "Running",
                                        DateTimeOffset.Parse("2026-06-17T10:00:00Z"),
                                        DateTimeOffset.Parse("2026-06-17T10:30:00Z"),
                                        null,
                                        null,
                                        null)))
                        : JsonContent.Create(new DoctorStatusResponse(
                            "My Project",
                            true,
                            1,
                        2,
                        3,
                        4,
                        "low",
                        "Graph drift: low",
                        false,
                        "Ollama",
                        768,
                        null))
                };
        }

        public async Task<JsonElement> ReadBodyAsync()
        {
            _body.Should().NotBeNull();
            using var doc = JsonDocument.Parse(_body!);
            return doc.RootElement.Clone();
        }
    }
}
