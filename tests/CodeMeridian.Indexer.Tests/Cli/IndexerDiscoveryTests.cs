using CodeMeridian.Indexer.Cli;
using FluentAssertions;

namespace CodeMeridian.Indexer.Tests.Cli;

public sealed class IndexerDiscoveryTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "codemeridian-indexer-tests",
        Guid.NewGuid().ToString("N"));

    public IndexerDiscoveryTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void ResolveProjectName_UsesPackageJsonNameFirst()
    {
        File.WriteAllText(Path.Combine(_root, "package.json"), """{"name":"my-web"}""");
        File.WriteAllText(Path.Combine(_root, "Other.sln"), "");

        var result = IndexerDiscovery.ResolveProjectName(new DirectoryInfo(_root));

        result.Should().Be("my-web");
    }

    [Fact]
    public void ResolveProjectName_FallsBackToSolutionName()
    {
        File.WriteAllText(Path.Combine(_root, "Payments.sln"), "");

        var result = IndexerDiscovery.ResolveProjectName(new DirectoryInfo(_root));

        result.Should().Be("Payments");
    }

    [Fact]
    public void ResolveProjectName_FallsBackToFolderName()
    {
        var result = IndexerDiscovery.ResolveProjectName(new DirectoryInfo(_root));

        result.Should().Be(Path.GetFileName(_root));
    }

    [Fact]
    public void ContainsFile_IgnoresDeclarationFiles()
    {
        File.WriteAllText(Path.Combine(_root, "types.d.ts"), "export interface User {}");

        var result = IndexerDiscovery.ContainsFile(new DirectoryInfo(_root), ".ts");

        result.Should().BeFalse();
    }

    [Fact]
    public void ContainsFile_SkipsGeneratedOutputDirectories()
    {
        var nodeModules = Directory.CreateDirectory(Path.Combine(_root, "node_modules"));
        File.WriteAllText(Path.Combine(nodeModules.FullName, "index.ts"), "export const ignored = true;");

        var result = IndexerDiscovery.ContainsFile(new DirectoryInfo(_root), ".ts");

        result.Should().BeFalse();
    }

    [Fact]
    public void FindTypeScriptRoots_ReturnsNearestTsconfigRoots()
    {
        var app = Directory.CreateDirectory(Path.Combine(_root, "apps", "web"));
        Directory.CreateDirectory(Path.Combine(app.FullName, "src"));
        File.WriteAllText(Path.Combine(app.FullName, "tsconfig.json"), "{}");
        File.WriteAllText(Path.Combine(app.FullName, "src", "index.ts"), "export const app = true;");

        var nested = Directory.CreateDirectory(Path.Combine(app.FullName, "src", "nested"));
        File.WriteAllText(Path.Combine(nested.FullName, "tsconfig.json"), "{}");
        File.WriteAllText(Path.Combine(nested.FullName, "feature.ts"), "export const nestedFeature = true;");

        var result = IndexerDiscovery.FindTypeScriptRoots(new DirectoryInfo(_root));

        result.Should().ContainSingle();
        result[0].FullName.Should().Be(app.FullName);
    }

    [Fact]
    public void FindTypeScriptRoots_FallsBackToRootWhenNoTsconfigExists()
    {
        File.WriteAllText(Path.Combine(_root, "index.ts"), "export const root = true;");

        var result = IndexerDiscovery.FindTypeScriptRoots(new DirectoryInfo(_root));

        result.Should().ContainSingle();
        result[0].FullName.Should().Be(_root);
    }

    [Fact]
    public void FindRepositoryRoot_WalksUpToSolutionFile()
    {
        File.WriteAllText(Path.Combine(_root, "CodeMeridian.sln"), "");
        var child = Directory.CreateDirectory(Path.Combine(_root, "src", "app"));

        var result = IndexerDiscovery.FindRepositoryRoot(child);

        result.Should().NotBeNull();
        result!.FullName.Should().Be(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
