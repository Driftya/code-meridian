using CodeMeridian.Tooling.Discovery;
using FluentAssertions;

namespace CodeMeridian.Indexer.Tests.Cli;

public sealed class RepositoryInventoryDiscoveryTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateDirectory(Path.Combine(
        Path.GetTempPath(), $"codemeridian-inventory-{Guid.NewGuid():N}"));
    private readonly RepositoryInventoryDiscovery _sut = new();

    [Fact]
    public void Discover_ExcludesGeneratedFilesAndIgnoredDirectories()
    {
        Write("src/App.cs");
        Write("src/App.g.cs");
        Write("src/App.generated.cs");
        Write("src/AssemblyInfo.cs");
        Write("node_modules/dependency/index.js");
        Write(".git/config");
        Write(".meridian/cache/profile.json");
        Write("bin/App.dll");
        Write("docs/guide.md");

        var inventory = _sut.Discover(_root);

        inventory.IsComplete.Should().BeTrue();
        inventory.Files.Should().Equal("docs/guide.md", "src/App.cs");
        inventory.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void Discover_PreservesPathsWithSpacesAndNonAsciiCharacters()
    {
        Write("manuscript/chapters/åva scene.md");
        Write("scripts/Publish Chapter.ps1");

        var inventory = _sut.Discover(_root);

        inventory.Files.Should().Equal("manuscript/chapters/åva scene.md", "scripts/Publish Chapter.ps1");
    }

    [Fact]
    public void Discover_EmptyRootIsComplete()
    {
        var inventory = _sut.Discover(_root);

        inventory.IsComplete.Should().BeTrue();
        inventory.Files.Should().BeEmpty();
    }

    [Fact]
    public void Discover_MissingRootDoesNotReturnAnEmptyCompleteInventory()
    {
        var missing = new DirectoryInfo(Path.Combine(_root.FullName, "missing"));

        var act = () => _sut.Discover(missing);

        act.Should().Throw<DirectoryNotFoundException>();
    }

    [Fact]
    public void Discover_PropagatesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var act = () => _sut.Discover(_root, cancellation.Token);

        act.Should().Throw<OperationCanceledException>();
    }

    private void Write(string path)
    {
        var fullPath = Path.Combine(_root.FullName, path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, "");
    }

    public void Dispose() => _root.Delete(recursive: true);
}
