using System.Text;
using CodeMeridian.Core.Projects;
using CodeMeridian.Indexer.Cli.Commands;
using FluentAssertions;

namespace CodeMeridian.Indexer.Tests.Cli;

public sealed class AnalyzedSourceSnapshotTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"analyzed-source-{Guid.NewGuid():N}"));

    [Fact]
    public void FingerprintUsesPathsKindsAndBytesWithDeterministicOrdering()
    {
        File.WriteAllText(Path.Combine(_root.FullName, "a.md"), "# A");
        File.WriteAllText(Path.Combine(_root.FullName, "b.md"), "# B");
        ProjectInventoryFile[] files = [new("a.md", "markdown"), new("b.md", "markdown")];
        var first = Capture(files);
        Capture(files.Reverse().ToArray()).Fingerprint.Should().Be(first.Fingerprint);
        Capture([files[0] with { Kind = "text" }, files[1]]).Fingerprint.Should().NotBe(first.Fingerprint);
        File.SetLastWriteTimeUtc(Path.Combine(_root.FullName, "a.md"), DateTime.UtcNow.AddDays(-1));
        first.IsCurrent(CancellationToken.None).Should().BeTrue();
        File.WriteAllText(Path.Combine(_root.FullName, "a.md"), "# C");
        Capture(files).Fingerprint.Should().NotBe(first.Fingerprint);
    }

    [Theory]
    [InlineData("changed")]
    [InlineData("deleted")]
    [InlineData("added")]
    [InlineData("renamed")]
    public void ContentAndInventoryChangesInvalidateTheSnapshot(string change)
    {
        var path = Path.Combine(_root.FullName, "a.md");
        File.WriteAllText(path, "# A");
        var source = Capture([new("a.md", "markdown")]);
        var timestamp = File.GetLastWriteTimeUtc(path);
        switch (change)
        {
            case "changed": File.WriteAllText(path, "# B"); File.SetLastWriteTimeUtc(path, timestamp); break;
            case "deleted": File.Delete(path); break;
            case "added": File.WriteAllText(Path.Combine(_root.FullName, "b.md"), "# B"); break;
            case "renamed": File.Move(path, Path.Combine(_root.FullName, "b.md")); break;
        }
        source.IsCurrent(CancellationToken.None).Should().BeFalse();
    }

    [Fact]
    public async Task GuardChecksTheBytesReturnedToTheParserAndRemembersTransientMismatches()
    {
        var file = new FileInfo(Path.Combine(_root.FullName, "a.md"));
        File.WriteAllText(file.FullName, "# A");
        var source = Capture([new("a.md", "markdown")]);
        File.WriteAllText(file.FullName, "# B");
        var read = () => source.ReadSourceAsync(file, CancellationToken.None);
        await read.Should().ThrowAsync<IOException>();
        File.WriteAllText(file.FullName, "# A");
        source.IsCurrent(CancellationToken.None).Should().BeFalse();
        source.AllDocumentsRead.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GuardPreservesUtf8AndUtf16DecodingAndRecordsEmptyDocuments(bool utf16)
    {
        var file = new FileInfo(Path.Combine(_root.FullName, "a.md"));
        var empty = new FileInfo(Path.Combine(_root.FullName, "empty.md"));
        File.WriteAllText(file.FullName, "# Räksmörgås", utf16 ? Encoding.Unicode : new UTF8Encoding(true));
        File.WriteAllText(empty.FullName, "");
        var source = Capture([new("a.md", "markdown"), new("empty.md", "markdown")]);
        (await source.ReadSourceAsync(file, CancellationToken.None)).Should().Be("# Räksmörgås");
        source.AllDocumentsRead.Should().BeFalse();
        (await source.ReadSourceAsync(empty, CancellationToken.None)).Should().BeEmpty();
        source.AllDocumentsRead.Should().BeTrue();
    }

    [Fact]
    public async Task ReadFailureCannotBecomeCurrentAfterTheFileReturns()
    {
        var file = new FileInfo(Path.Combine(_root.FullName, "a.md"));
        File.WriteAllText(file.FullName, "# A");
        var source = Capture([new("a.md", "markdown")]);
        File.Delete(file.FullName);
        var read = () => source.ReadSourceAsync(file, CancellationToken.None);
        await read.Should().ThrowAsync<IOException>();
        File.WriteAllText(file.FullName, "# A");
        source.IsCurrent(CancellationToken.None).Should().BeFalse();
    }

    [Fact]
    public void UnreadableInputsCannotBeCaptured()
    {
        var path = Path.Combine(_root.FullName, "a.md");
        File.WriteAllText(path, "# A");
        using var exclusive = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var capture = () => Capture([new("a.md", "markdown")]);
        capture.Should().Throw<IOException>();
    }

    private AnalyzedSourceSnapshot Capture(ProjectInventoryFile[] files) => AnalyzedSourceSnapshot.Capture(_root, files, CancellationToken.None);
    public void Dispose() => _root.Delete(recursive: true);
}
