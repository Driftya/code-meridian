using System.Net.Http.Json;

namespace CodeMeridian.Sdk;

public sealed partial class CodeMeridianClient
{
    public async Task PublishPowerShellGraphAsync<TSnapshot>(TSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync("/api/v1/knowledge/powershell", snapshot, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
