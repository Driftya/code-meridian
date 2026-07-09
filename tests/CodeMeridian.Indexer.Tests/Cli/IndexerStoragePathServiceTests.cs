using CodeMeridian.Tooling.Storage;
using FluentAssertions;

namespace CodeMeridian.Indexer.Tests.Cli;

public sealed class IndexerStoragePathServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "codemeridian-storage-path-tests",
        Guid.NewGuid().ToString("N"));

    public IndexerStoragePathServiceTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void ResolveCacheDirectory_UsesRepoPathForRepositoryMode()
    {
        var service = new IndexerStoragePathService();
        var directory = service.ResolveCacheDirectory(new DirectoryInfo(_root), "MyApi", IndexerStorageMode.Repository);

        directory.FullName.Should().EndWith(Path.Combine(".meridian", "cache"));
        directory.FullName.Should().StartWith(_root);
    }

    [Fact]
    public void ResolveCacheDirectory_UsesGlobalRootForGlobalMode()
    {
        var service = new IndexerStoragePathService();
        var directory = service.ResolveCacheDirectory(new DirectoryInfo(_root), "MyApi", IndexerStorageMode.Global);

        directory.FullName.Should().Contain(Path.Combine("projects", service.ResolveProjectKey(new DirectoryInfo(_root), "MyApi"), "cache"));
    }

    [Fact]
    public void ResolveProjectKey_UsesProjectNameWhenAvailable()
    {
        var service = new IndexerStoragePathService();
        var key = service.ResolveProjectKey(new DirectoryInfo(_root), "My Project");

        key.Should().StartWith("my-project-");
    }

    [Fact]
    public void ResolveProjectKey_FallsBackToFolderNameWhenProjectNameIsBlankAndNoGitMetadataExists()
    {
        var root = Directory.CreateDirectory(Path.Combine(_root, "My Fancy Root"));
        var service = new IndexerStoragePathService();

        var key = service.ResolveProjectKey(root, "   ");

        key.Should().StartWith("my-fancy-root-");
    }

    [Fact]
    public void ResolveProjectKey_NormalizesProjectNameSegments()
    {
        var service = new IndexerStoragePathService();

        var key = service.ResolveProjectKey(new DirectoryInfo(_root), " My__Project !! Name ");

        key.Should().StartWith("my-project-name-");
        key.Should().NotContain("--");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
