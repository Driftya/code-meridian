using CodeMeridian.Indexer.Cli.Commands;
using FluentAssertions;

namespace CodeMeridian.Indexer.Tests.Cli;

public sealed class TypeScriptIndexerProcessRunnerTests
{
    [Fact]
    public void ResolveTsxCommand_ReturnsNullWhenBinaryIsMissing()
    {
        using var workspace = TestWorkspace.Create();

        TypeScriptIndexerProcessRunner.ResolveTsxCommand(workspace.Root).Should().BeNull();
    }

    [Fact]
    public async Task EnsureDependenciesAsync_ReturnsZeroWhenLocalTsxExists()
    {
        using var workspace = TestWorkspace.Create();
        workspace.WriteFile("node_modules/.bin/tsx.cmd", "echo");

        var result = await TypeScriptIndexerProcessRunner.EnsureDependenciesAsync(workspace.Root);

        result.Should().Be(0);
    }

    [Fact]
    public async Task EnsureDependenciesAsync_ReturnsZeroWhenPackageJsonIsMissing()
    {
        using var workspace = TestWorkspace.Create();

        var result = await TypeScriptIndexerProcessRunner.EnsureDependenciesAsync(workspace.Root);

        result.Should().Be(0);
    }

    [Fact]
    public void ResolveTsxCommand_ReturnsBinaryPathWhenPresent()
    {
        using var workspace = TestWorkspace.Create();
        var binary = workspace.WriteFile(@"node_modules/.bin/tsx.cmd", "echo");

        TypeScriptIndexerProcessRunner.ResolveTsxCommand(workspace.Root)
            .Should()
            .Be(binary.FullName);
    }

    private sealed class TestWorkspace : IDisposable
    {
        private TestWorkspace(DirectoryInfo root) => Root = root;

        public DirectoryInfo Root { get; }

        public static TestWorkspace Create()
        {
            var root = new DirectoryInfo(Path.Combine(Path.GetTempPath(), $"codemeridian-ts-runner-{Guid.NewGuid():N}"));
            root.Create();
            return new TestWorkspace(root);
        }

        public FileInfo WriteFile(string relativePath, string content)
        {
            var file = new FileInfo(Path.Combine(Root.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            file.Directory!.Create();
            File.WriteAllText(file.FullName, content);
            return file;
        }

        public void Dispose()
        {
            if (Root.Exists)
                Root.Delete(recursive: true);
        }
    }
}
