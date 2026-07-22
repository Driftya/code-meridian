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
    public async Task RunAsync_WhenCapabilitiesAreRequested_PrintsCapabilitiesWithoutNeedingAProjectRoot()
    {
        var settings = CreateSettings(new DirectoryInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))), clear: false, incremental: true, listCapabilities: true);
        var handler = CreateHandler(settings);

        using var output = new StringWriter();
        var originalOut = Console.Out;
        try
        {
            Console.SetOut(output);
            var exitCode = await handler.RunAsync();

            exitCode.Should().Be(0);
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        output.ToString().Should().Contain("CodeMeridian indexer capabilities");
        output.ToString().Should().Contain("Commands:");
    }

    [Fact]
    public async Task RunAsync_WhenRootDoesNotExist_ReturnsError()
    {
        var root = new DirectoryInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        var handler = CreateHandler(CreateSettings(root, clear: false, incremental: true));

        using var errors = new StringWriter();
        var originalError = Console.Error;
        try
        {
            Console.SetError(errors);
            var exitCode = await handler.RunAsync();

            exitCode.Should().Be(1);
        }
        finally
        {
            Console.SetError(originalError);
        }

        errors.ToString().Should().Contain("directory not found");
    }

    [Fact]
    public async Task RunAsync_WhenNoIndexersMatch_ReturnsSuccessfullyWithGuidance()
    {
        using var workspace = TestWorkspace.Create();
        var settings = CreateSettings(workspace.Root, clear: false, incremental: true, skipTypeScript: true, skipCSharp: true, skipConfiguration: true);
        var handler = CreateHandler(settings);

        using var output = new StringWriter();
        var originalOut = Console.Out;
        try
        {
            Console.SetOut(output);
            var exitCode = await handler.RunAsync();

            exitCode.Should().Be(0);
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        output.ToString().Should().Contain("No enabled indexers found matching this project.");
        output.ToString().Should().Contain("--list-capabilities");
    }

    [Fact]
    public async Task RunAsync_WhenOnlyDocumentationIsEnabled_RecognizesTheDocumentationIndexer()
    {
        using var workspace = TestWorkspace.Create();
        workspace.WriteFile("README.md", "# docs only\n");
        var settings = CreateSettings(
            workspace.Root,
            clear: false,
            incremental: true,
            dryRun: true,
            skipTypeScript: true,
            skipCSharp: true,
            skipConfiguration: true,
            includeDocs: true);
        var handler = CreateHandler(settings);

        using var output = new StringWriter();
        var originalOut = Console.Out;
        try
        {
            Console.SetOut(output);
            var exitCode = await handler.RunAsync();

            exitCode.Should().Be(0);
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        output.ToString().Should().Contain("Dry run:");
        output.ToString().Should().Contain("Include docs      : True");
        output.ToString().Should().NotContain("No enabled indexers found");
    }

    [Fact]
    public async Task RunAsync_WhenDryRunIsEnabled_PrintsSelectedIndexerDetails()
    {
        using var workspace = TestWorkspace.Create();
        workspace.WriteFile("tsconfig.json", "{}");
        workspace.WriteFile("src/app.ts", "export const value = 1;\n");
        var settings = CreateSettings(workspace.Root, clear: false, incremental: true, dryRun: true);
        var handler = CreateHandler(settings);

        using var output = new StringWriter();
        var originalOut = Console.Out;
        try
        {
            Console.SetOut(output);
            var exitCode = await handler.RunAsync();

            exitCode.Should().Be(0);
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        output.ToString().Should().Contain("Dry run:");
        output.ToString().Should().Contain("TypeScript roots");
        output.ToString().Should().Contain("TypeScript roots  : 1");
    }

    [Fact]
    public async Task RunAsync_WhenIncrementalCacheHasNoChanges_SkipsTheIndexPass()
    {
        using var workspace = TestWorkspace.Create();
        workspace.WriteFile("tsconfig.json", "{}");
        workspace.WriteFile("src/app.ts", "export const value = 1;\n");
        var settings = CreateSettings(workspace.Root, clear: false, incremental: true);
        var storagePathService = new IndexerStoragePathService();
        var cacheDirectory = storagePathService.ResolveCacheDirectory(workspace.Root, settings.Project, settings.StorageMode);
        var cache = IncrementalIndexCache.Load(cacheDirectory, settings.Project);
        var indexableFiles = IndexExecutionPlanBuilder.EnumerateIndexableFiles(
            workspace.Root,
            includeCSharp: false,
            includeTypeScript: true,
            includeDocs: false,
            includeConfiguration: false);
        cache.Save(IndexExecutionPlanBuilder.BuildPlan(cache, workspace.Root, indexableFiles, forceFull: true));
        var handler = CreateHandler(settings);
        using var output = new StringWriter();
        var originalOut = Console.Out;
        try
        {
            Console.SetOut(output);
            var exitCode = await handler.RunAsync();

            exitCode.Should().Be(0);
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        output.ToString().Should().Contain("No file changes detected since the last successful index run.");
    }

    [Fact]
    public void FileSelectionMethods_KeepOnlyFilesBelongingToEachLanguageRoot()
    {
        using var workspace = TestWorkspace.Create();
        var sourceRoot = Directory.CreateDirectory(Path.Combine(workspace.Root.FullName, "src"));
        var tsFile = workspace.WriteFile("src/app.ts", "export const value = 1;\n");
        var cssFile = workspace.WriteFile("src/app.css", ".app {}\n");
        var outsideFile = workspace.WriteFile("other/app.ts", "export const value = 2;\n");
        var handler = CreateHandler(CreateSettings(workspace.Root, clear: false, incremental: true));

        var filterTypeScriptFiles = typeof(IndexCommandHandler).GetMethod(
            "FilterTypeScriptFiles",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var selectHtmlCssFiles = typeof(IndexCommandHandler).GetMethod(
            "SelectHtmlCssFilesForRoot",
            BindingFlags.Instance | BindingFlags.NonPublic);

        filterTypeScriptFiles.Should().NotBeNull();
        selectHtmlCssFiles.Should().NotBeNull();

        var filtered = (IEnumerable<string>)filterTypeScriptFiles!.Invoke(
            handler,
            new object[] { new[] { tsFile.FullName, cssFile.FullName, outsideFile.FullName }, sourceRoot })!;
        var selected = (IEnumerable<string>)selectHtmlCssFiles!.Invoke(handler, new object?[] { null, sourceRoot })!;

        filtered.Should().ContainSingle().Which.Should().Be(tsFile.FullName);
        selected.Should().ContainSingle().Which.Should().Be(cssFile.FullName);
    }

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
    public void BuildExecutionContext_WhenDocsAreDisabled_PreservesTheirCachedStateWithoutDeletingThem()
    {
        using var workspace = TestWorkspace.Create();
        workspace.WriteFile("tsconfig.json", "{}");
        var sourceFile = workspace.WriteFile("src/app.ts", "export const value = 1;\n");
        var documentation = workspace.WriteFile("docs/guide.md", "# first\n");
        var settings = CreateSettings(workspace.Root, clear: false, incremental: true);
        var storagePathService = new IndexerStoragePathService();
        var cacheDirectory = storagePathService.ResolveCacheDirectory(workspace.Root, settings.Project, settings.StorageMode);
        var cache = IncrementalIndexCache.Load(cacheDirectory, settings.Project);
        cache.Save(cache.BuildPlan(workspace.Root, [sourceFile, documentation], forceFull: true));
        workspace.WriteFile("docs/guide.md", "# changed while docs are disabled\n");

        var handler = CreateHandler(settings);
        var buildExecutionContext = typeof(IndexCommandHandler).GetMethod(
            "BuildExecutionContext",
            BindingFlags.Instance | BindingFlags.NonPublic);

        var context = buildExecutionContext!.Invoke(handler, [false]);
        var contextType = context!.GetType();
        var plan = (IncrementalIndexPlan)contextType
            .GetProperty("IncrementalPlan", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .GetValue(context)!;
        var deletedFiles = (IReadOnlyCollection<string>)contextType
            .GetProperty("DeletedFiles", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .GetValue(context)!;

        plan.ChangedFiles.Should().BeEmpty();
        deletedFiles.Should().BeEmpty();
        plan.NextState.Select(entry => entry.Path).Should().Contain("docs/guide.md");
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

    [Fact]
    public void WriteHtmlCssBatchFile_WritesRelativePathsAndClassifiedRoles()
    {
        using var workspace = TestWorkspace.Create();
        var htmlFile = workspace.WriteFile("src/app.html", "<div class=\"hero\"></div>");
        var scssFile = workspace.WriteFile("styles/site.scss", ".hero { color: red; }");
        var cacheDirectory = new DirectoryInfo(Path.Combine(workspace.Root.FullName, ".meridian", "cache"));
        var classifier = IndexedFileRoleClassifierFactory.Create(snapshot: null);
        var handler = CreateHandler(CreateSettings(workspace.Root, clear: false, incremental: true));

        var method = typeof(IndexCommandHandler).GetMethod(
            "WriteHtmlCssBatchFile",
            BindingFlags.Instance | BindingFlags.NonPublic);

        method.Should().NotBeNull();
        var batchFile = (FileInfo?)method!.Invoke(handler, [cacheDirectory, workspace.Root, new[] { htmlFile.FullName, scssFile.FullName }, classifier]);

        batchFile.Should().NotBeNull();
        batchFile!.Exists.Should().BeTrue();

        var payload = JsonSerializer.Deserialize<TypeScriptBatchEntry[]>(File.ReadAllText(batchFile.FullName));
        payload.Should().NotBeNull();
        payload.Should().BeEquivalentTo(
        [
            new TypeScriptBatchEntry("src/app.html", "Unknown"),
            new TypeScriptBatchEntry("styles/site.scss", "Unknown"),
        ]);
    }

    [Fact]
    public void BuildExecutionContext_FindsHtmlCssRootsWhenNoTypeScriptRootExists()
    {
        using var workspace = TestWorkspace.Create();
        workspace.WriteFile("src/app.html", "<div class=\"hero\"></div>");
        workspace.WriteFile("styles/site.scss", ".hero { color: red; }");

        var handler = CreateHandler(CreateSettings(workspace.Root, clear: false, incremental: true));
        var buildExecutionContext = typeof(IndexCommandHandler).GetMethod(
            "BuildExecutionContext",
            BindingFlags.Instance | BindingFlags.NonPublic);

        buildExecutionContext.Should().NotBeNull();
        var context = buildExecutionContext!.Invoke(handler, [false]);

        context.Should().NotBeNull();
        var hasHtmlCss = (bool)context!
            .GetType()
            .GetProperty("HasHtmlCss", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .GetValue(context)!;
        var htmlCssRoots = (IReadOnlyList<DirectoryInfo>)context
            .GetType()
            .GetProperty("HtmlCssRoots", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .GetValue(context)!;

        hasHtmlCss.Should().BeTrue();
        htmlCssRoots.Should().ContainSingle();
        htmlCssRoots[0].FullName.Should().Be(workspace.Root.FullName);
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
        string? apiKey = null,
        bool listCapabilities = false,
        bool dryRun = false,
        bool skipTypeScript = false,
        bool skipCSharp = true,
        bool skipConfiguration = true,
        bool includeDocs = false)
        => new()
        {
            RootPath = root,
            Project = "CodeMeridian",
            CodeMeridianUrl = "http://127.0.0.1:5100",
            ApiKey = apiKey,
            Clear = clear,
            RebuildKeywords = false,
            IncludeDocs = includeDocs,
            Watch = false,
            DryRun = dryRun,
            ListCapabilities = listCapabilities,
            SkipCSharp = skipCSharp,
            SkipTypeScript = skipTypeScript,
            SkipConfiguration = skipConfiguration,
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
