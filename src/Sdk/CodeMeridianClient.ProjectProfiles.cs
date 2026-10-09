using System.Net.Http.Json;

namespace CodeMeridian.Sdk;

public sealed partial class CodeMeridianClient
{
    public async Task<long> BeginProjectProfileAsync(string projectContext, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync("/api/v1/project-profiles/begin", new { projectContext }, cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<ProjectProfileGeneration>(cancellationToken: cancellationToken);
        if (result is null || result.Generation <= 0) throw new InvalidOperationException("The server did not issue a valid profile generation.");
        return result.Generation;
    }

    public async Task PublishProjectProfileAsync<TSnapshot>(TSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync("/api/v1/project-profiles/", snapshot, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<ProjectProfileResponse> GetProjectProfileAsync(
        string projectContext, string? targetPath = null, CancellationToken cancellationToken = default)
    {
        var path = "/api/v1/project-profiles/?projectContext=" + Uri.EscapeDataString(projectContext);
        if (targetPath is not null) path += "&targetPath=" + Uri.EscapeDataString(targetPath);
        using var response = await httpClient.GetAsync(path, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ProjectProfileResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("The server returned no project profile result.");
    }
}
