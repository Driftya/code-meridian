using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CodeMeridian.Evolution.Application.Sensors;
using Microsoft.Extensions.Options;

namespace CodeMeridian.Evolution.Infrastructure.Sensors;

public sealed class CodeMeridianGraphSensor(
    IHttpClientFactory httpClientFactory,
    IOptions<CodeMeridianSensorOptions> options,
    TimeProvider timeProvider) : ISensor
{
    private const string ProjectNodeQuery = """
        query EvolutionProjectNodes($projectContext: String!, $limit: Int!) {
          nodes(
            filter: {
              labels: ["CodeNode"]
              projectContext: $projectContext
            }
            sort: { field: "filePath", direction: ASCENDING }
            limit: $limit
          ) {
            id
            name
            type
            filePath
            primaryLabel
          }
        }
        """;

    public string Id => "codemeridian-graph";

    public string DisplayName => "CodeMeridian project graph";

    public async Task<SensorHealth> CheckHealthAsync(
        CancellationToken cancellationToken = default)
    {
        if (!options.Value.Enabled)
        {
            return new SensorHealth(false, "disabled", timeProvider.GetUtcNow());
        }

        try
        {
            var client = CreateClient();
            using var response = await client
                .GetAsync("health", cancellationToken)
                .ConfigureAwait(false);
            return new SensorHealth(
                response.IsSuccessStatusCode,
                response.IsSuccessStatusCode ? "ready" : $"http-{(int)response.StatusCode}",
                timeProvider.GetUtcNow());
        }
        catch (HttpRequestException)
        {
            return new SensorHealth(false, "unreachable", timeProvider.GetUtcNow());
        }
    }

    public async Task<IReadOnlyList<SensorObservation>> CollectAsync(
        CancellationToken cancellationToken = default)
    {
        if (!options.Value.Enabled)
        {
            return [];
        }

        var client = CreateClient();
        var requestBody = new
        {
            query = ProjectNodeQuery,
            variables = new
            {
                projectContext = options.Value.ProjectContext,
                limit = Math.Clamp(options.Value.MaximumNodes, 1, 100)
            }
        };
        using var response = await client
            .PostAsJsonAsync("graphql", requestBody, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var content = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var document = await JsonDocument
            .ParseAsync(content, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (document.RootElement.TryGetProperty("errors", out var errors) &&
            errors.GetArrayLength() > 0)
        {
            throw new InvalidOperationException(
                "CodeMeridian rejected the read-only project graph query.");
        }

        if (!document.RootElement.TryGetProperty("data", out var data) ||
            !data.TryGetProperty("nodes", out var nodes))
        {
            return [];
        }

        var observedAt = timeProvider.GetUtcNow();
        var observations = nodes.EnumerateArray()
            .Select(node =>
            {
                var name = GetString(node, "name") ?? "CodeMeridian graph node";
                var path = GetString(node, "filePath");
                var label = GetString(node, "primaryLabel") ?? GetString(node, "type");
                var summary = string.IsNullOrWhiteSpace(path)
                    ? $"{label}: {name}"
                    : $"{label}: {name} at {path}";
                var id = GetString(node, "id") ?? summary;
                return new SensorObservation(
                    StableId(id),
                    "code-graph-node",
                    summary,
                    "information",
                    observedAt,
                    0.9m)
                {
                    ProjectId = options.Value.TargetProjectId,
                    TrustLevel = "authenticated-code-graph",
                    SourceUri = new Uri(
                        new Uri(options.Value.BaseUrl, UriKind.Absolute),
                        "graphql").AbsoluteUri
                };
            })
            .ToArray();
        return Array.AsReadOnly(observations);
    }

    private HttpClient CreateClient()
    {
        var client = httpClientFactory.CreateClient("evolution-codemeridian");
        client.BaseAddress = new Uri(
            options.Value.BaseUrl.TrimEnd('/') + "/",
            UriKind.Absolute);

        if (!string.IsNullOrWhiteSpace(options.Value.ApiKey))
        {
            client.DefaultRequestHeaders.Remove("X-CodeMeridian-ApiKey");
            client.DefaultRequestHeaders.Add(
                "X-CodeMeridian-ApiKey",
                options.Value.ApiKey);
        }

        return client;
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) &&
               value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static string StableId(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return $"codemeridian:{Convert.ToHexString(hash).ToLowerInvariant()}";
    }
}
