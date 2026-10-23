namespace CodeMeridian.Sdk;

public sealed partial class CodeMeridianClient
{
    public async Task<string> SearchDocumentationAsync(string query, string? projectContext = null,
        string? rootPath = null, string? targetPath = null, CancellationToken cancellationToken = default)
    {
        var path = "/api/v1/knowledge/documents/search?query=" + Uri.EscapeDataString(query);
        if (projectContext is not null) path += "&projectContext=" + Uri.EscapeDataString(projectContext);
        if (rootPath is not null) path += "&rootPath=" + Uri.EscapeDataString(rootPath);
        if (targetPath is not null) path += "&targetPath=" + Uri.EscapeDataString(targetPath);
        using var response = await httpClient.GetAsync(path, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }
}
