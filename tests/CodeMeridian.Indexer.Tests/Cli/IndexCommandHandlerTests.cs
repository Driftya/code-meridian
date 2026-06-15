using System.Reflection;
using System.Text.Json;
using CodeMeridian.Indexer.Cli.Commands;
using CodeMeridian.RoslynIndexer.Pipeline;
using CodeMeridian.Tooling.Discovery;
using CodeMeridian.Tooling.Storage;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace CodeMeridian.Indexer.Tests.Cli;

public sealed class IndexCommandHandlerTests
{
    [Fact]
    public void BuildExecutionContext_WhenWatchReentryDisablesClear_ComputesIncrementalChanges()
    {
        using var workspace = TestWorkspace.Create();
        workspace.WriteFile("tsconfig.json", "{}");
        var sourceFile = workspace.WriteFile("src/app.ts", "export const value = 1;\n");

        var settings = CreateSettings(workspace.Root, clear: true, incremental: true);
        var storagePathService = new IndexerStoragePathService();
        var cacheDirectory = storagePathService.ResolveCacheDirectory(workspace.Root, settings.Project, settings.StorageMode);
        var cache = IncrementalIndexCache.Load(cacheDirectory, settings.Project);
        var initialFiles = IndexExecutionPlanBuilder.EnumerateIndexableFiles(
            workspace.Root,
            includeCSharp: false,
            includeTypeScript: true,
            includeDocs: false,
            includeConfiguration: false);
        cache.Save(IndexExecutionPlanBuilder.BuildPlan(cache, workspace.Root, initialFiles, forceFull: true));

        File.WriteAllText(sourceFile.FullName, "export const value = 2;\n");

        var handler = CreateHandler(settings);
        var buildExecutionContext = typeof(IndexCommandHandler).GetMethod(
            "BuildExecutionContext",
            BindingFlags.Instance | BindingFlags.NonPublic);

        buildExecutionContext.Should().NotBeNull();
        var context = buildExecutionContext!.Invoke(handler, [false]);

        context.Should().NotBeNull();
        var changedFiles = (IReadOnlyCollection<string>?)context!
            .GetType()
            .GetProperty("ChangedFiles", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .GetValue(context);

        changedFiles.Should().ContainSingle("src/app.ts");
    }

    [Fact]
    public void WriteTypeScriptBatchFile_WritesRelativePathsAndClassifiedRoles()
    {
        using var workspace = TestWorkspace.Create();
        workspace.WriteFile("tsconfig.json", "{}");
        var configFile = workspace.WriteFile("src/config/AppConfig.ts", "export const value = 1;\n");
        var testFile = workspace.WriteFile("tests/app/order.spec.ts", "export const spec = true;\n");
        var cacheDirectory = new DirectoryInfo(Path.Combine(workspace.Root.FullName, ".meridian", "cache"));
        var classifier = IndexedFileRoleClassifierFactory.Create(snapshot: null);
        var handler = CreateHandler(CreateSettings(workspace.Root, clear: false, incremental: true));

        var method = typeof(IndexCommandHandler).GetMethod(
            "WriteTypeScriptBatchFile",
            BindingFlags.Instance | BindingFlags.NonPublic);

        method.Should().NotBeNull();
        var batchFile = (FileInfo?)method!.Invoke(handler, [cacheDirectory, workspace.Root, new[] { configFile.FullName, testFile.FullName }, classifier]);

        batchFile.Should().NotBeNull();
        batchFile!.Exists.Should().BeTrue();

        var payload = JsonSerializer.Deserialize<TypeScriptBatchEntry[]>(File.ReadAllText(batchFile.FullName));
        payload.Should().NotBeNull();
        payload.Should().BeEquivalentTo(
        [
            new TypeScriptBatchEntry("src/config/AppConfig.ts", "Configuration"),
            new TypeScriptBatchEntry("tests/app/order.spec.ts", "Test"),
        ]);
    }

    [Fact]
    public void CreateTypeScriptIndexerEnvironment_IncludesApiKeyWhenPresent()
    {
        using var workspace = TestWorkspace.Create();
        var handler = CreateHandler(CreateSettings(workspace.Root, clear: false, incremental: true, apiKey: "secret-key"));

        var method = typeof(IndexCommandHandler).GetMethod(
            "CreateTypeScriptIndexerEnvironment",
            BindingFlags.Instance | BindingFlags.NonPublic);

        method.Should().NotBeNull();
        var environment = (IReadOnlyDictionary<string, string?>?)method!.Invoke(handler, []);

        environment.Should().ContainSingle()
            .Which.Should().Be(new KeyValuePair<string, string?>("CodeMeridian_Auth_ApiKey", "secret-key"));
    }

    private static IndexCommandHandler CreateHandler(ResolvedIndexerSettings settings)
        => new(
            Options.Create(settings),
            new ProjectDiscoveryService(),
            new IndexerStoragePathService(),
            new DiagnosticsCommand(new ProjectDiscoveryService()));

    private static ResolvedIndexerSettings CreateSettings(
        DirectoryInfo root,
        bool clear,
        bool incremental,
        string? apiKey = null)
        => new()
        {
            RootPath = root,
            Project = "CodeMeridian",
            CodeMeridianUrl = "http://127.0.0.1:5100",
            ApiKey = apiKey,
            Clear = clear,
            RebuildKeywords = false,
            IncludeDocs = false,
            Watch = false,
            DryRun = false,
            ListCapabilities = false,
            SkipCSharp = true,
            SkipTypeScript = false,
            SkipConfiguration = true,
            ConfigurationFiles = null,
            FileRoles = null,
            SkipDiagnostics = true,
            AllowRepoScripts = false,
            Incremental = incremental,
            StorageMode = IndexerStorageMode.Repository,
            HasOutdatedLocalConfig = false,
            LocalConfigVersion = 0,
            CurrentConfigVersion = 0
        };

    private sealed record TypeScriptBatchEntry(string Path, string FileRole);

    private sealed class TestWorkspace : IDisposable
    {
        private TestWorkspace(DirectoryInfo root) => Root = root;

        public DirectoryInfo Root { get; }

        public static TestWorkspace Create()
        {
            var root = new DirectoryInfo(Path.Combine(Path.GetTempPath(), $"codemeridian-index-handler-{Guid.NewGuid():N}"));
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
