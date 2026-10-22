using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using CodeMeridian.Indexer.Cli.Commands;
using CodeMeridian.Tooling.Discovery;
using CodeMeridian.Tooling.Storage;
using FluentAssertions;

namespace CodeMeridian.Indexer.Tests.Cli;

public sealed class ProjectRevisionReaderTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"profile-revision-{Guid.NewGuid():N}"));

    [Fact]
    public void MissingRepositoryAndUnbornHeadRemainUnknown()
    {
        ProjectRevisionReader.Read(_root).Should().BeNull();
        Git("init", "--quiet");
        ProjectRevisionReader.Read(_root).Should().BeNull();
    }

    [Fact]
    public void ReadsCleanDirtyUntrackedAndDetachedHeadWithoutOptionalIndexWrites()
    {
        Git("init", "--quiet");
        File.WriteAllText(Path.Combine(_root.FullName, "README.md"), "# Original");
        Git("add", "README.md");
        Git("-c", "user.name=Fixture", "-c", "user.email=fixture@example.invalid", "-c", "commit.gpgsign=false", "commit", "--quiet", "--no-verify", "-m", "Fixture");
        var indexPath = Path.Combine(_root.FullName, ".git", "index");
        var index = File.ReadAllBytes(indexPath);
        var clean = ProjectRevisionReader.Read(_root)!;
        clean.Validate().Should().BeNull();
        clean.Dirty.Should().BeFalse();
        File.ReadAllBytes(indexPath).Should().Equal(index);
        File.WriteAllText(Path.Combine(_root.FullName, "README.md"), "# Edited");
        ProjectRevisionReader.Read(_root)!.Dirty.Should().BeTrue();
        Git("checkout", "--", "README.md");
        File.WriteAllText(Path.Combine(_root.FullName, "untracked.md"), "# Untracked");
        ProjectRevisionReader.Read(_root)!.Dirty.Should().BeTrue();
        File.Delete(Path.Combine(_root.FullName, "untracked.md"));
        Git("checkout", "--quiet", "--detach", clean.Commit);
        var detached = ProjectRevisionReader.Read(_root)!;
        detached.Commit.Should().Be(clean.Commit);
        detached.Dirty.Should().BeFalse();
    }

    [Fact]
    public void CancellationIsPropagated()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var read = () => ProjectRevisionReader.Read(_root, cancellation.Token);
        read.Should().Throw<OperationCanceledException>();
    }

    [Fact]
    public void DiscoveryDoesNotExecuteAConfiguredFsmonitorHook()
    {
        Git("init", "--quiet");
        File.WriteAllText(Path.Combine(_root.FullName, "README.md"), "# Fixture");
        Git("add", "README.md");
        Git("-c", "user.name=Fixture", "-c", "user.email=fixture@example.invalid", "-c", "commit.gpgsign=false", "commit", "--quiet", "--no-verify", "-m", "Fixture");
        var hook = Path.Combine(_root.FullName, ".git", "monitor-hook");
        var marker = Path.Combine(_root.FullName, "hook-ran");
        File.WriteAllText(hook, "#!/bin/sh\necho ran > '" + marker.Replace('\\', '/') + "'\n");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(hook, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        Git("config", "core.fsmonitor", "\"" + hook.Replace('\\', '/') + "\"");

        ProjectRevisionReader.Read(_root)!.Dirty.Should().BeFalse();

        File.Exists(marker).Should().BeFalse();
    }

    [Fact]
    public async Task NewCommitInvalidatesReuseWhileOrdinaryDirtyEditsRemainEligibleForDeltas()
    {
        Git("init", "--quiet");
        var file = Path.Combine(_root.FullName, "README.md");
        File.WriteAllText(file, "# Fixture");
        Git("add", "README.md");
        Git("-c", "user.name=Fixture", "-c", "user.email=fixture@example.invalid", "-c", "commit.gpgsign=false", "commit", "--quiet", "--no-verify", "-m", "Fixture");
        var settings = new ResolvedIndexerSettings { RootPath = _root, Project = "Fixture", CodeMeridianUrl = "http://localhost",
            StorageMode = IndexerStorageMode.Repository, SkipCSharp = true, SkipTypeScript = true, SkipConfiguration = true };
        var snapshot = new LocalProjectProfileBuilder(new RepositoryInventoryDiscovery()).BuildSnapshot(settings, 3);
        snapshot = snapshot with { Profile = snapshot.Profile with { SourceFingerprint = "sha256:" + new string('a', 64) } };
        var cache = IncrementalIndexCache.Load(new DirectoryInfo(Path.Combine(_root.FullName, ".meridian", "cache")), "Fixture");
        cache.Save(cache.BuildPlan(_root, [new FileInfo(file)], forceFull: true), settings.CodeMeridianUrl, 3, snapshot.Profile.ObservedAt);
        var reporter = new IndexProfileRunReporter(new(new RepositoryInventoryDiscovery()))
        {
            HttpClientFactory = (_, _) => new HttpClient(new ProfileHandler(snapshot.Profile)) { BaseAddress = new Uri(settings.CodeMeridianUrl) }
        };
        (await reporter.IsCacheCurrentAsync(settings, cache)).Should().BeTrue();
        File.WriteAllText(file, "# Edited");
        (await reporter.IsCacheCurrentAsync(settings, cache)).Should().BeTrue();
        Git("-c", "user.name=Fixture", "-c", "user.email=fixture@example.invalid", "-c", "commit.gpgsign=false", "commit", "--allow-empty", "--quiet", "--no-verify", "-m", "Next");

        (await reporter.IsCacheCurrentAsync(settings, cache)).Should().BeFalse();
    }

    private sealed class ProfileHandler(CodeMeridian.Core.Projects.ProjectProfile profile) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new
            {
                status = "available", project = "Fixture", generation = 3, latestGeneration = 3, profile, contractVersion = "1.0"
            }) });
    }

    private void Git(params string[] arguments)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = _root.FullName, UseShellExecute = false,
            CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Task.WhenAll(output, error).GetAwaiter().GetResult();
        process.ExitCode.Should().Be(0, error.GetAwaiter().GetResult());
    }

    public void Dispose()
    {
        foreach (var file in _root.EnumerateFiles("*", SearchOption.AllDirectories)) file.Attributes = FileAttributes.Normal;
        _root.Delete(recursive: true);
    }
}
