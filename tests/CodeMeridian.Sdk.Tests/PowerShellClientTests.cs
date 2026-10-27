using System.Net;
using CodeMeridian.Sdk;
using FluentAssertions;

namespace CodeMeridian.Sdk.Tests;

public sealed class PowerShellClientTests
{
    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.Conflict)]
    public async Task PublicationUsesAuthenticatedClientAndPropagatesFailure(HttpStatusCode status)
    {
        using var http = new HttpClient(new Handler(status)) { BaseAddress = new("http://localhost") };
        http.DefaultRequestHeaders.Authorization = new("Bearer", "test-key");
        var publish = () => new CodeMeridianClient(http).PublishPowerShellGraphAsync(new { generation = 3 });
        if (status == HttpStatusCode.OK) await publish.Should().NotThrowAsync();
        else await publish.Should().ThrowAsync<HttpRequestException>();
    }

    private sealed class Handler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.RequestUri!.AbsolutePath.Should().Be("/api/v1/knowledge/powershell");
            request.Headers.Authorization!.Parameter.Should().Be("test-key");
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(status));
        }
    }
}
