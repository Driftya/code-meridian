using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CodeMeridian.Sdk;
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
    public async Task RebuildKeywordGraphAsync_SendsProjectContextBody()
    {
        var handler = new CapturingHandler();
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost")
        };
        var sut = new CodeMeridianClient(client);

        await sut.RebuildKeywordGraphAsync("CodeMeridian");

        handler.Request.Should().NotBeNull();
        handler.Request!.Method.Should().Be(HttpMethod.Post);
        handler.Request.RequestUri!.AbsolutePath.Should().Be("/api/v1/knowledge/keywords/rebuild");

        var body = await handler.ReadBodyAsync();
        body.GetProperty("projectContext").GetString().Should().Be("CodeMeridian");
    }

    [Fact]
    public async Task ClassifyKeywordsAsync_SendsProjectContextBody()
    {
        var handler = new CapturingHandler();
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost")
        };
        var sut = new CodeMeridianClient(client);

        await sut.ClassifyKeywordsAsync("CodeMeridian");

        handler.Request.Should().NotBeNull();
        handler.Request!.Method.Should().Be(HttpMethod.Post);
        handler.Request.RequestUri!.AbsolutePath.Should().Be("/api/v1/knowledge/keywords/classify");

        var body = await handler.ReadBodyAsync();
        body.GetProperty("projectContext").GetString().Should().Be("CodeMeridian");
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        private string? _body;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            _body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = JsonContent.Create(new DoctorStatusResponse(
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
