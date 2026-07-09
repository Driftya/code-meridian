using CodeMeridian.Tooling.Discovery;
using FluentAssertions;

namespace CodeMeridian.Indexer.Tests.Cli;

public sealed class IndexerDiscoveryTests : IDisposable
{
    private readonly ProjectDiscoveryService _service = new();
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

        var result = _service.ResolveProjectName(new DirectoryInfo(_root));

        result.Should().Be("my-web");
    }

    [Fact]
    public void ResolveProjectName_FallsBackToSolutionName()
    {
        File.WriteAllText(Path.Combine(_root, "Payments.sln"), "");

        var result = _service.ResolveProjectName(new DirectoryInfo(_root));

        result.Should().Be("Payments");
    }

    [Fact]
    public void ResolveProjectName_FallsBackToSlnxBeforeWorkspace()
    {
        File.WriteAllText(Path.Combine(_root, "Payments.slnx"), "");
        File.WriteAllText(Path.Combine(_root, "Payments.code-workspace"), "");

        var result = _service.ResolveProjectName(new DirectoryInfo(_root));

        result.Should().Be("Payments");
    }

    [Fact]
    public void ResolveProjectName_FallsBackToWorkspaceName()
    {
        File.WriteAllText(Path.Combine(_root, "Payments.code-workspace"), "");

        var result = _service.ResolveProjectName(new DirectoryInfo(_root));

        result.Should().Be("Payments");
    }

    [Fact]
    public void ResolveProjectName_FallsBackToFolderName()
    {
        var result = _service.ResolveProjectName(new DirectoryInfo(_root));

        result.Should().Be(Path.GetFileName(_root));
    }

    [Fact]
    public void ContainsFile_IgnoresDeclarationFiles()
    {
        File.WriteAllText(Path.Combine(_root, "types.d.ts"), "export interface User {}");

        var result = _service.ContainsFile(new DirectoryInfo(_root), ".ts");

        result.Should().BeFalse();
    }

    [Fact]
    public void ContainsFile_SkipsGeneratedOutputDirectories()
    {
        var nodeModules = Directory.CreateDirectory(Path.Combine(_root, "node_modules"));
        File.WriteAllText(Path.Combine(nodeModules.FullName, "index.ts"), "export const ignored = true;");

        var result = _service.ContainsFile(new DirectoryInfo(_root), ".ts");

        result.Should().BeFalse();
    }

    [Fact]
    public void ContainsFile_SkipsMeridianCacheDirectories()
    {
        var meridianCache = Directory.CreateDirectory(Path.Combine(_root, ".meridian", "cache"));
        File.WriteAllText(Path.Combine(meridianCache.FullName, "index.ts"), "export const ignored = true;");

        var result = _service.ContainsFile(new DirectoryInfo(_root), ".ts");

        result.Should().BeFalse();
    }

    [Fact]
    public void ContainsFile_SkipsGeneratedCSharpFiles()
    {
        File.WriteAllText(Path.Combine(_root, "Generated.g.cs"), "class Generated {}");
        File.WriteAllText(Path.Combine(_root, "Feature.generated.cs"), "class GeneratedPartial {}");
        File.WriteAllText(Path.Combine(_root, "AssemblyInfo.cs"), "class AssemblyInfo {}");

        var result = _service.ContainsFile(new DirectoryInfo(_root), ".cs");

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

        var result = _service.FindTypeScriptRoots(new DirectoryInfo(_root));

        result.Should().ContainSingle();
        result[0].FullName.Should().Be(app.FullName);
    }

    [Fact]
    public void FindTypeScriptRoots_FallsBackToRootWhenNoTsconfigExists()
    {
        File.WriteAllText(Path.Combine(_root, "index.ts"), "export const root = true;");

        var result = _service.FindTypeScriptRoots(new DirectoryInfo(_root));

        result.Should().ContainSingle();
        result[0].FullName.Should().Be(_root);
    }

    [Fact]
    public void FindTypeScriptRoots_IgnoresTsconfigDirectoriesWithoutRealTypeScriptFiles()
    {
        var app = Directory.CreateDirectory(Path.Combine(_root, "apps", "web"));
        File.WriteAllText(Path.Combine(app.FullName, "tsconfig.json"), "{}");
        File.WriteAllText(Path.Combine(app.FullName, "types.d.ts"), "export interface User {}");

        var result = _service.FindTypeScriptRoots(new DirectoryInfo(_root));

        result.Should().BeEmpty();
    }

    [Fact]
    public void FindRepositoryRoot_WalksUpToSolutionFile()
    {
        File.WriteAllText(Path.Combine(_root, "CodeMeridian.sln"), "");
        var child = Directory.CreateDirectory(Path.Combine(_root, "src", "app"));

        var result = _service.FindRepositoryRoot(child);

        result.Should().NotBeNull();
        result!.FullName.Should().Be(_root);
    }

    [Fact]
    public void FindRepositoryRoot_ReturnsNullWhenSolutionFileIsMissing()
    {
        var child = Directory.CreateDirectory(Path.Combine(_root, "src", "app"));

        var result = _service.FindRepositoryRoot(child);

        result.Should().BeNull();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
