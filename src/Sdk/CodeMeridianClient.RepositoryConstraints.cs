using System.Globalization;
using System.Net.Http.Json;

namespace CodeMeridian.Sdk;

public sealed partial class CodeMeridianClient
{
    public async Task<RepositoryConstraintResponse> FindRepositoryConstraintViolationsAsync(
        string projectContext, string? targetPath = null, int findingLimit = 100, CancellationToken cancellationToken = default)
    {
        var path = "/api/v1/repository-constraints?projectContext=" + Uri.EscapeDataString(projectContext)
            + "&findingLimit=" + findingLimit.ToString(CultureInfo.InvariantCulture);
        if (targetPath is not null) path += "&targetPath=" + Uri.EscapeDataString(targetPath);
        using var response = await httpClient.GetAsync(path, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<RepositoryConstraintResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("The server returned no repository constraint result.");
    }
}
