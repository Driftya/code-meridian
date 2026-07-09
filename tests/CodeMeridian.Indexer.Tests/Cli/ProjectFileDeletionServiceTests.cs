using CodeMeridian.Indexer.Cli.Commands;
using FluentAssertions;
using System.Net;

namespace CodeMeridian.Indexer.Tests.Cli;

public sealed class ProjectFileDeletionServiceTests
{
    [Fact]
    public void NormalizeRelativePaths_DeduplicatesAndNormalizesRootedPaths()
    {
        using var workspace = TestWorkspace.Create();

        var result = ProjectFileDeletionService.NormalizeRelativePaths(
            ["docs/guide.md", Path.Combine(workspace.Root.FullName, "docs", "guide.md"), @"docs\other.md"],
            workspace.Root);

        result.Should().BeEquivalentTo(["docs/guide.md", "docs/other.md"]);
    }

    [Fact]
    public async Task DeleteAsync_WhenNoPaths_DoesNotRequireValidServerUrl()
    {
        using var workspace = TestWorkspace.Create();
        var sut = new ProjectFileDeletionService("not-a-valid-uri", apiKey: null, "CodeMeridian", workspace.Root);

        var act = () => sut.DeleteAsync([]);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DeleteAsync_SendsDeleteRequestsWithNormalizedPathsAndBearerToken()
    {
        using var workspace = TestWorkspace.Create();
        using var listener = new HttpListener();
        var prefix = $"http://127.0.0.1:{GetFreePort()}/";
        listener.Prefixes.Add(prefix);
        listener.Start();

        var requestsTask = CaptureRequestsAsync(listener, expectedCount: 2);
        var sut = new ProjectFileDeletionService(prefix, "secret-token", "CodeMeridian", workspace.Root);

        await sut.DeleteAsync(
        [
            Path.Combine(workspace.Root.FullName, "docs", "guide.md"),
            @"docs\other.md",
            "docs/guide.md"
        ]);

        var requests = await requestsTask;

        requests.Should().HaveCount(2);
        requests.Should().OnlyContain(request => request.Authorization == "Bearer secret-token");
        requests.Select(request => request.Path).Should().BeEquivalentTo(
        [
            "/api/v1/knowledge/project/CodeMeridian/files/docs%2Fguide.md",
            "/api/v1/knowledge/project/CodeMeridian/files/docs%2Fother.md"
        ]);
    }

    private static async Task<IReadOnlyList<CapturedRequest>> CaptureRequestsAsync(HttpListener listener, int expectedCount)
    {
        var requests = new List<CapturedRequest>();

        for (var i = 0; i < expectedCount; i++)
        {
            var context = await listener.GetContextAsync();
            requests.Add(new CapturedRequest(
                context.Request.HttpMethod,
                context.Request.RawUrl ?? string.Empty,
                context.Request.Headers["Authorization"]));
            context.Response.StatusCode = 200;
            context.Response.Close();
        }

        return requests;
    }

    private static int GetFreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed record CapturedRequest(string Method, string Path, string? Authorization);

    private sealed class TestWorkspace : IDisposable
    {
        private TestWorkspace(DirectoryInfo root) => Root = root;

        public DirectoryInfo Root { get; }

        public static TestWorkspace Create()
        {
            var root = new DirectoryInfo(Path.Combine(Path.GetTempPath(), $"codemeridian-delete-service-{Guid.NewGuid():N}"));
            root.Create();
            return new TestWorkspace(root);
        }

        public void Dispose()
        {
            if (Root.Exists)
                Root.Delete(recursive: true);
        }
    }
}
