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

    [Fact]
    public async Task RunAsync_PassesConfiguredEnvironmentVariablesToChildProcess()
    {
        if (!OperatingSystem.IsWindows())
            return;

        using var workspace = TestWorkspace.Create();
        var script = workspace.WriteFile(
            "verify-env.ps1",
            """
            if ($env:CODEMERIDIAN_TEST_KEY -eq 'secret-value') {
                exit 0
            }

            exit 7
            """);

        var exitCode = await TypeScriptIndexerProcessRunner.RunAsync(
            "powershell",
            ["-NoProfile", "-File", script.FullName],
            workspace.Root,
            new Dictionary<string, string?> { ["CODEMERIDIAN_TEST_KEY"] = "secret-value" });

        exitCode.Should().Be(0);
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
