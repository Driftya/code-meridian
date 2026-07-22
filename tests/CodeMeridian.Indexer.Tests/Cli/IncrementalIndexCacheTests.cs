using CodeMeridian.Tooling.Storage;
using FluentAssertions;
using System.Text.Json;

namespace CodeMeridian.Indexer.Tests.Cli;

public sealed class IncrementalIndexCacheTests
{
    [Fact]
    public void BuildPlan_WhenCacheIsEmpty_TreatsAllFilesAsChanged()
    {
        using var workspace = TestWorkspace.Create();
        var file = workspace.WriteFile("src/App.cs", "class App {}");
        var cacheDirectory = workspace.GetRepoCacheDirectory();

        var sut = IncrementalIndexCache.Load(cacheDirectory, "Project");

        var plan = sut.BuildPlan(workspace.Root, [file], forceFull: false);

        plan.ChangedFiles.Should().ContainSingle("src/App.cs");
        plan.DeletedFiles.Should().BeEmpty();
    }

    [Fact]
    public void BuildPlan_AfterSave_ReturnsOnlyModifiedAndDeletedFiles()
    {
        using var workspace = TestWorkspace.Create();
        var unchanged = workspace.WriteFile("src/Unchanged.cs", "class Unchanged {}");
        var changed = workspace.WriteFile("src/Changed.cs", "class Changed {}");
        var deleted = workspace.WriteFile("src/Deleted.cs", "class Deleted {}");
        var cacheDirectory = workspace.GetRepoCacheDirectory();

        var sut = IncrementalIndexCache.Load(cacheDirectory, "Project");
        var firstPlan = sut.BuildPlan(workspace.Root, [unchanged, changed, deleted], forceFull: false);
        sut.Save(firstPlan);

        Thread.Sleep(20);
        changed = workspace.WriteFile("src/Changed.cs", "class Changed { void M() {} }");
        File.Delete(deleted.FullName);

        var reloaded = IncrementalIndexCache.Load(cacheDirectory, "Project");
        var nextPlan = reloaded.BuildPlan(workspace.Root, [unchanged, changed], forceFull: false);

        nextPlan.ChangedFiles.Should().ContainSingle("src/Changed.cs");
        nextPlan.DeletedFiles.Should().ContainSingle("src/Deleted.cs");
    }

    [Fact]
    public void BuildPlan_WhenTimestampChangesButContentIsSame_DoesNotTreatFileAsChanged()
    {
        using var workspace = TestWorkspace.Create();
        var file = workspace.WriteFile("src/App.cs", "class App {}");
        var cacheDirectory = workspace.GetRepoCacheDirectory();

        var sut = IncrementalIndexCache.Load(cacheDirectory, "Project");
        var firstPlan = sut.BuildPlan(workspace.Root, [file], forceFull: false);
        sut.Save(firstPlan);

        file.LastWriteTimeUtc = file.LastWriteTimeUtc.AddDays(1);
        file.Refresh();

        var reloaded = IncrementalIndexCache.Load(cacheDirectory, "Project");
        var nextPlan = reloaded.BuildPlan(workspace.Root, [file], forceFull: false);

        nextPlan.ChangedFiles.Should().BeEmpty();
        nextPlan.DeletedFiles.Should().BeEmpty();
    }

    [Fact]
    public void Load_WhenCacheVersionIsStale_TreatsAllFilesAsChanged()
    {
        using var workspace = TestWorkspace.Create();
        var file = workspace.WriteFile("src/App.cs", "class App {}");
        var cacheDirectory = workspace.GetRepoCacheDirectory();
        var initial = IncrementalIndexCache.Load(cacheDirectory, "Project");
        var initialPlan = initial.BuildPlan(workspace.Root, [file], forceFull: false);
        initial.Save(initialPlan);

        var cacheFile = cacheDirectory.GetFiles("indexer-files-*.json").Should().ContainSingle().Subject;
        using var document = JsonDocument.Parse(File.ReadAllText(cacheFile.FullName));
        var root = document.RootElement.Clone();

        File.WriteAllText(
            cacheFile.FullName,
            JsonSerializer.Serialize(new
            {
                Version = 2,
                Files = root.GetProperty("Files")
            }));

        var sut = IncrementalIndexCache.Load(cacheDirectory, "Project");
        var plan = sut.BuildPlan(workspace.Root, [file], forceFull: false);

        plan.ChangedFiles.Should().ContainSingle("src/App.cs");
    }

    [Fact]
    public void BuildPlan_WhenAnIndexerIsDisabled_PreservesItsCacheEntriesUntilItIsEnabledAgain()
    {
        using var workspace = TestWorkspace.Create();
        var source = workspace.WriteFile("src/App.cs", "class App {}");
        var documentation = workspace.WriteFile("docs/guide.md", "# first");
        var cacheDirectory = workspace.GetRepoCacheDirectory();
        var initial = IncrementalIndexCache.Load(cacheDirectory, "Project");
        initial.Save(initial.BuildPlan(workspace.Root, [source, documentation], forceFull: true));

        documentation = workspace.WriteFile("docs/guide.md", "# changed while docs are disabled");
        var sourceOnly = IncrementalIndexCache.Load(cacheDirectory, "Project");
        var sourcePlan = sourceOnly.BuildPlan(
            workspace.Root,
            [source],
            forceFull: false,
            path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase));

        sourcePlan.ChangedFiles.Should().BeEmpty();
        sourcePlan.DeletedFiles.Should().BeEmpty();
        sourcePlan.NextState.Select(entry => entry.Path).Should().BeEquivalentTo("src/App.cs", "docs/guide.md");
        sourceOnly.Save(sourcePlan);

        var allIndexers = IncrementalIndexCache.Load(cacheDirectory, "Project");
        var resumedPlan = allIndexers.BuildPlan(workspace.Root, [source, documentation], forceFull: false);

        resumedPlan.ChangedFiles.Should().ContainSingle("docs/guide.md");
        resumedPlan.DeletedFiles.Should().BeEmpty();
    }

    private sealed class TestWorkspace : IDisposable
    {
        private TestWorkspace(DirectoryInfo root)
        {
            Root = root;
        }

        public DirectoryInfo Root { get; }

        public static TestWorkspace Create()
        {
            var root = new DirectoryInfo(Path.Combine(Path.GetTempPath(), $"codemeridian-cache-{Guid.NewGuid():N}"));
            root.Create();
            return new TestWorkspace(root);
        }

        public FileInfo WriteFile(string relativePath, string content)
        {
            var file = new FileInfo(Path.Combine(Root.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            file.Directory!.Create();
            File.WriteAllText(file.FullName, content);
            file.Refresh();
            return file;
        }

        public DirectoryInfo GetRepoCacheDirectory()
        {
            var directory = new DirectoryInfo(Path.Combine(Root.FullName, ".meridian", "cache"));
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
