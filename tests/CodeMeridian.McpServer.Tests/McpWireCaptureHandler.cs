using System.Collections.Concurrent;

namespace CodeMeridian.McpServer.Tests;

internal sealed class McpWireCaptureHandler : DelegatingHandler
{
    private readonly ConcurrentQueue<McpWireExchange> _exchanges = new();

    public IReadOnlyList<McpWireExchange> Exchanges => _exchanges.ToArray();

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var requestBody = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken);
        var requestHeaders = request.Headers
            .Where(header => !header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(
                header => header.Key,
                header => string.Join(",", header.Value),
                StringComparer.OrdinalIgnoreCase);

        var response = await base.SendAsync(request, cancellationToken);
        var responseBody = response.Content is null
            ? string.Empty
            : await response.Content.ReadAsStringAsync(cancellationToken);
        var responseHeaders = response.Headers.ToDictionary(
            header => header.Key,
            header => string.Join(",", header.Value),
            StringComparer.OrdinalIgnoreCase);

        if (response.Content is not null)
        {
            var replacement = new StringContent(responseBody);
            foreach (var header in response.Content.Headers)
            {
                replacement.Headers.Remove(header.Key);
                replacement.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
            response.Content = replacement;
        }

        _exchanges.Enqueue(new McpWireExchange(
            request.Method.Method,
            request.RequestUri?.PathAndQuery ?? string.Empty,
            requestHeaders,
            requestBody,
            (int)response.StatusCode,
            responseHeaders,
            responseBody));
        return response;
    }
}

internal sealed record McpWireExchange(
    string HttpMethod,
    string Path,
    IReadOnlyDictionary<string, string> RequestHeaders,
    string RequestBody,
    int StatusCode,
    IReadOnlyDictionary<string, string> ResponseHeaders,
    string ResponseBody);
