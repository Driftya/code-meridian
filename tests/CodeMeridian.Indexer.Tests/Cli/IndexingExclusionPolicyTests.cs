using CodeMeridian.Tooling.Discovery;
using FluentAssertions;

namespace CodeMeridian.Indexer.Tests.Cli;

public sealed class IndexingExclusionPolicyTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "codemeridian-indexing-exclusion-tests",
        Guid.NewGuid().ToString("N"));

    public IndexingExclusionPolicyTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Theory]
    [InlineData(".git")]
    [InlineData(".GIT")]
    [InlineData("node_modules")]
    [InlineData("coverage")]
    public void IsIgnoredDirectoryName_MatchesKnownNamesCaseInsensitively(string directoryName)
    {
        IndexingExclusionPolicy.IsIgnoredDirectoryName(directoryName).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("src")]
    public void IsIgnoredDirectoryName_RejectsUnknownOrBlankNames(string? directoryName)
    {
        IndexingExclusionPolicy.IsIgnoredDirectoryName(directoryName).Should().BeFalse();
    }

    [Theory]
    [InlineData("src/Generated.g.cs")]
    [InlineData("src/Feature.generated.cs")]
    [InlineData("Properties/AssemblyInfo.cs")]
    [InlineData("node_modules/pkg/index.ts")]
    [InlineData(".meridian/cache/index.json")]
    public void IsIgnoredRelativePath_DetectsIgnoredPatterns(string relativePath)
    {
        IndexingExclusionPolicy.IsIgnoredRelativePath(relativePath).Should().BeTrue();
    }

    [Theory]
    [InlineData("src/App.cs")]
    [InlineData("docs/guide.generated.md")]
    [InlineData("src/AssemblyInformation.cs")]
    public void IsIgnoredRelativePath_LeavesSupportedPathsVisible(string relativePath)
    {
        IndexingExclusionPolicy.IsIgnoredRelativePath(relativePath).Should().BeFalse();
    }

    [Fact]
    public void IsIgnoredPath_UsesRelativePathFromRoot()
    {
        var root = new DirectoryInfo(_root);
        var file = new FileInfo(Path.Combine(_root, "src", "Feature.generated.cs"));
        file.Directory!.Create();
        File.WriteAllText(file.FullName, "class Generated {}");

        var result = IndexingExclusionPolicy.IsIgnoredPath(root, file);

        result.Should().BeTrue();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
