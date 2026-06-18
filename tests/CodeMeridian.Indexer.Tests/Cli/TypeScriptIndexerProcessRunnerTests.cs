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
    public async Task EnsureDependenciesAsync_UsesInstallWhenPackageLockIsMissing()
    {
        using var workspace = TestWorkspace.Create();
        workspace.WriteFile("package.json", """{"name":"test"}""");
        var invocations = new List<string[]>();

        var result = await TypeScriptIndexerProcessRunner.EnsureDependenciesAsync(
            workspace.Root,
            (fileName, arguments, workingDirectory, environmentVariables) =>
            {
                invocations.Add(arguments.ToArray());
                return Task.FromResult(0);
            });

        result.Should().Be(0);
        invocations.Should().ContainSingle().Which.Should().Equal("install");
    }

    [Fact]
    public async Task EnsureDependenciesAsync_FallsBackToInstallWhenCiFails()
    {
        using var workspace = TestWorkspace.Create();
        workspace.WriteFile("package.json", """{"name":"test"}""");
        workspace.WriteFile("package-lock.json", """{"name":"test","lockfileVersion":3}""");
        var invocations = new List<string[]>();

        var result = await TypeScriptIndexerProcessRunner.EnsureDependenciesAsync(
            workspace.Root,
            (fileName, arguments, workingDirectory, environmentVariables) =>
            {
                var captured = arguments.ToArray();
                invocations.Add(captured);
                return Task.FromResult(captured[0] == "ci" ? 1 : 0);
            });

        result.Should().Be(0);
        invocations.Should().HaveCount(2);
        invocations[0].Should().Equal("ci");
        invocations[1].Should().Equal("install");
    }

    [Fact]
    public async Task EnsureDependenciesAsync_DoesNotFallbackWhenCiSucceeds()
    {
        using var workspace = TestWorkspace.Create();
        workspace.WriteFile("package.json", """{"name":"test"}""");
        workspace.WriteFile("package-lock.json", """{"name":"test","lockfileVersion":3}""");
        var invocations = new List<string[]>();

        var result = await TypeScriptIndexerProcessRunner.EnsureDependenciesAsync(
            workspace.Root,
            (fileName, arguments, workingDirectory, environmentVariables) =>
            {
                invocations.Add(arguments.ToArray());
                return Task.FromResult(0);
            });

        result.Should().Be(0);
        invocations.Should().ContainSingle().Which.Should().Equal("ci");
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

        TypeScriptIndexerProcessRunner.ResolveTsxCommand(workspace.Root)
            .Should()
            .Be(expectedBinary.FullName);
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
