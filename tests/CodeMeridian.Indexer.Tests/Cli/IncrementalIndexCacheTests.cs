using CodeMeridian.Indexer.Cli;
using FluentAssertions;

namespace CodeMeridian.Indexer.Tests.Cli;

public sealed class IncrementalIndexCacheTests
{
    [Fact]
    public void BuildPlan_WhenCacheIsEmpty_TreatsAllFilesAsChanged()
    {
        using var workspace = TestWorkspace.Create();
        var file = workspace.WriteFile("src/App.cs", "class App {}");

        var sut = IncrementalIndexCache.Load(workspace.Root, "Project");

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

        var sut = IncrementalIndexCache.Load(workspace.Root, "Project");
        var firstPlan = sut.BuildPlan(workspace.Root, [unchanged, changed, deleted], forceFull: false);
        sut.Save(firstPlan);

        Thread.Sleep(20);
        changed = workspace.WriteFile("src/Changed.cs", "class Changed { void M() {} }");
        File.Delete(deleted.FullName);

        var reloaded = IncrementalIndexCache.Load(workspace.Root, "Project");
        var nextPlan = reloaded.BuildPlan(workspace.Root, [unchanged, changed], forceFull: false);

        nextPlan.ChangedFiles.Should().ContainSingle("src/Changed.cs");
        nextPlan.DeletedFiles.Should().ContainSingle("src/Deleted.cs");
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

        public void Dispose()
        {
            if (Root.Exists)
                Root.Delete(recursive: true);
        }
    }
}
