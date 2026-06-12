using System.Net.Http.Headers;
using CodeMeridian.Sdk;

namespace CodeMeridian.Indexer.Cli.Commands;

internal sealed class ProjectFileDeletionService(string codeMeridianUrl, string? apiKey, string project, DirectoryInfo rootPath)
{
    public async Task DeleteAsync(IEnumerable<string> relativePaths)
    {
        var distinct = NormalizeRelativePaths(relativePaths, rootPath);

        if (distinct.Count == 0)
            return;

        using var httpClient = new HttpClient
        {
            BaseAddress = new Uri(codeMeridianUrl),
            Timeout = TimeSpan.FromMinutes(10)
        };
        httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
        if (!string.IsNullOrWhiteSpace(apiKey))
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var client = new CodeMeridianClient(httpClient);
        foreach (var relativePath in distinct)
            await client.DeleteProjectFileAsync(project, relativePath);
    }

    internal static IReadOnlyList<string> NormalizeRelativePaths(IEnumerable<string> relativePaths, DirectoryInfo rootPath)
    {
        return relativePaths
            .Select(path => Path.IsPathRooted(path)
                ? Path.GetRelativePath(rootPath.FullName, path).Replace('\\', '/')
                : path.Replace('\\', '/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
