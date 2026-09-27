using CodeMeridian.Indexer.Cli.Commands;
using FluentAssertions;

namespace CodeMeridian.Indexer.Tests.Cli;

public sealed class NodeIndexerProcessRunnerTests
{
    [Theory]
    [InlineData("TsIndexer", false)]
    [InlineData("TsIndexer", true)]
    [InlineData("HtmlCssIndexer", false)]
    [InlineData("HtmlCssIndexer", true)]
    public async Task EnsureDependenciesAsync_PropagatesInstallFailure(string worker, bool hasLockfile)
    {
        using var workspace = TestWorkspace.Create();
        var indexerRoot = workspace.CreateDirectory($"tools/{worker}");
        workspace.WriteFile($"tools/{worker}/package.json", """{"name":"test"}""");
        if (hasLockfile)
            workspace.WriteFile($"tools/{worker}/package-lock.json", """{"lockfileVersion":3}""");
        var invocations = new List<string[]>();

        var result = await NodeIndexerProcessRunner.EnsureDependenciesAsync(
            indexerRoot,
            (fileName, arguments, workingDirectory, environmentVariables) =>
            {
                workingDirectory.Should().Be(indexerRoot);
                invocations.Add(arguments.ToArray());
                return Task.FromResult(arguments[0] == "ci" ? 1 : 7);
            });

        result.Should().Be(7);
        invocations.Should().HaveCount(hasLockfile ? 2 : 1);
        invocations[^1].Should().Equal("install", "--legacy-peer-deps", "--include=dev", "--include=optional");
    }

    [Fact]
    public void ResolveTsxCommand_ReturnsNullWhenBinaryIsMissing()
    {
        using var workspace = TestWorkspace.Create();

        NodeIndexerProcessRunner.ResolveTsxCommand(workspace.Root).Should().BeNull();
    }

    [Fact]
    public void ResolveTsxCommand_ReturnsBinaryPathWhenPresent()
    {
        using var workspace = TestWorkspace.Create();
        var expectedBinary = OperatingSystem.IsWindows()
            ? workspace.WriteFile(@"node_modules/.bin/tsx.cmd", "echo")
            : workspace.WriteFile(@"node_modules/.bin/tsx", "echo");

        workspace.WriteFile(@"node_modules/.bin/tsx", "echo");
        workspace.WriteFile(@"node_modules/.bin/tsx.cmd", "echo");

        NodeIndexerProcessRunner.ResolveTsxCommand(workspace.Root)
            .Should()
            .Be(expectedBinary.FullName);
    }

    [Fact]
    public void ResolveTsxCommand_FindsWorkspaceHoistedBinaryInParentDirectory()
    {
        using var workspace = TestWorkspace.Create();
        var indexerRoot = workspace.CreateDirectory("tools/IndexerShared");
        var expectedBinary = OperatingSystem.IsWindows()
            ? workspace.WriteFile(@"node_modules/.bin/tsx.cmd", "echo")
            : workspace.WriteFile(@"node_modules/.bin/tsx", "echo");

        NodeIndexerProcessRunner.ResolveTsxCommand(indexerRoot)
            .Should()
            .Be(expectedBinary.FullName);
    }

    private sealed class TestWorkspace : IDisposable
    {
        private TestWorkspace(DirectoryInfo root) => Root = root;

        public DirectoryInfo Root { get; }

        public static TestWorkspace Create()
        {
            var root = new DirectoryInfo(Path.Combine(Path.GetTempPath(), $"codemeridian-node-runner-{Guid.NewGuid():N}"));
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

        public DirectoryInfo CreateDirectory(string relativePath)
        {
            var directory = new DirectoryInfo(Path.Combine(Root.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            directory.Create();
            return directory;
        }

        public void Dispose()
        {
            if (Root.Exists)
                Root.Delete(recursive: true);
        }
    }
}
