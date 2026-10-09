using System.Net;
using System.Net.Http.Json;
using CodeMeridian.Sdk;
using FluentAssertions;

namespace CodeMeridian.Sdk.Tests;

public sealed class ProjectProfileClientTests
{
    [Fact]
    public async Task ProfileOperationsUseAuthenticatedClientAndEncodeTarget()
    {
        var paths = new List<string>();
        using var http = new HttpClient(new Handler(request =>
        {
            request.Headers.Authorization!.Parameter.Should().Be("test-key");
            paths.Add(request.RequestUri!.PathAndQuery);
            return paths.Count switch
            {
                1 => new(HttpStatusCode.OK) { Content = JsonContent.Create(new { generation = 12 }) },
                2 => new(HttpStatusCode.OK),
                _ => new(HttpStatusCode.OK) { Content = JsonContent.Create(new {
                    status = "unknown", project = "Docs & Notes", warnings = Array.Empty<string>(), contractVersion = "1.0"
                }) }
            };
        })) { BaseAddress = new Uri("http://localhost") };
        http.DefaultRequestHeaders.Authorization = new("Bearer", "test-key");
        var client = new CodeMeridianClient(http);
        (await client.BeginProjectProfileAsync("Docs & Notes")).Should().Be(12);
        await client.PublishProjectProfileAsync(new { generation = 12 });
        (await client.GetProjectProfileAsync("Docs & Notes", "docs/a & b.md")).Status.Should().Be("unknown");
        paths.Should().Equal("/api/v1/project-profiles/begin", "/api/v1/project-profiles/",
            "/api/v1/project-profiles/?projectContext=Docs%20%26%20Notes&targetPath=docs%2Fa%20%26%20b.md");
    }

    [Fact]
    public async Task PublicationConflictPropagatesInsteadOfReportingSuccess()
    {
        using var http = new HttpClient(new Handler(_ => new(HttpStatusCode.Conflict))) { BaseAddress = new Uri("http://localhost") };
        var publish = () => new CodeMeridianClient(http).PublishProjectProfileAsync(new { generation = 1 });
        await publish.Should().ThrowAsync<HttpRequestException>();
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(respond(request));
        }
    }
}
