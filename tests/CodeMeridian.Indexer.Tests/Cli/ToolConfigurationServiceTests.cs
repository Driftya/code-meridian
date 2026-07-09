using CodeMeridian.Tooling.Configuration;
using CodeMeridian.Tooling.Discovery;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace CodeMeridian.Indexer.Tests.Cli;

public sealed class ToolConfigurationServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "codemeridian-tool-config-tests",
        Guid.NewGuid().ToString("N"));

    public ToolConfigurationServiceTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void ResolveProject_PrefersExplicitOverride()
    {
        var sut = CreateSut();
        var context = new ToolConfigurationContext(
            new DirectoryInfo(_root),
            new CodeMeridianConfigSnapshot("LocalProject", "http://local", false, false, null, null, null, 1),
            new CodeMeridianConfigSnapshot("GlobalProject", "http://global", false, false, null, null, null, 1),
            "EnvProject",
            "http://env",
            "secret");

        var result = sut.ResolveProject(context, " OverrideProject ");

        result.Should().Be("OverrideProject");
    }

    [Fact]
    public void ResolveProject_UsesDiscoveryFallbackWhenNoConfiguredProjectExists()
    {
        var sut = CreateSut(projectName: "DiscoveredProject");
        var context = new ToolConfigurationContext(
            new DirectoryInfo(_root),
            null,
            null,
            null,
            null,
            null);

        var result = sut.ResolveProject(context, null);

        result.Should().Be("DiscoveredProject");
    }

    [Fact]
    public void ResolveProject_CanSkipFallbackDiscovery()
    {
        var sut = CreateSut(projectName: "DiscoveredProject");
        var context = new ToolConfigurationContext(
            new DirectoryInfo(_root),
            null,
            null,
            null,
            null,
            null);

        var result = sut.ResolveProject(context, null, includeFallback: false);

        result.Should().BeEmpty();
    }

    [Fact]
    public void ResolveCodeMeridianUrl_UsesOverrideThenEnvironmentThenConfigsThenDefault()
    {
        var sut = CreateSut(defaultUrl: "http://default");
        var context = new ToolConfigurationContext(
            new DirectoryInfo(_root),
            new CodeMeridianConfigSnapshot("LocalProject", "http://local", false, false, null, null, null, 1),
            new CodeMeridianConfigSnapshot("GlobalProject", "http://global", false, false, null, null, null, 1),
            "EnvProject",
            "http://env",
            null);

        sut.ResolveCodeMeridianUrl(context, " http://override ").Should().Be("http://override");
        sut.ResolveCodeMeridianUrl(context, null).Should().Be("http://env");
        sut.ResolveCodeMeridianUrl(
            context with { EnvironmentUrl = null },
            null).Should().Be("http://local");
        sut.ResolveCodeMeridianUrl(
            context with { EnvironmentUrl = null, LocalConfig = null },
            null).Should().Be("http://global");
        sut.ResolveCodeMeridianUrl(
            context with { EnvironmentUrl = null, LocalConfig = null, GlobalConfig = null },
            null).Should().Be("http://default");
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void ResolveAllowRepoScripts_ReturnsTrueWhenAnySourceEnablesIt(bool overrideValue, bool localValue, bool globalValue)
    {
        var sut = CreateSut();
        var context = new ToolConfigurationContext(
            new DirectoryInfo(_root),
            new CodeMeridianConfigSnapshot("LocalProject", null, localValue, false, null, null, null, 1),
            new CodeMeridianConfigSnapshot("GlobalProject", null, globalValue, false, null, null, null, 1),
            null,
            null,
            null);

        var result = sut.ResolveAllowRepoScripts(context, overrideValue);

        result.Should().BeTrue();
    }

    [Fact]
    public void ResolveAllowRepoScripts_ReturnsFalseWhenNoSourceEnablesIt()
    {
        var sut = CreateSut();
        var context = new ToolConfigurationContext(
            new DirectoryInfo(_root),
            new CodeMeridianConfigSnapshot("LocalProject", null, false, false, null, null, null, 1),
            new CodeMeridianConfigSnapshot("GlobalProject", null, false, false, null, null, null, 1),
            null,
            null,
            null);

        var result = sut.ResolveAllowRepoScripts(context, allowRepoScriptsOverride: false);

        result.Should().BeFalse();
    }

    [Fact]
    public void ResolveRootPath_UsesCurrentDirectoryWhenPathIsMissing()
    {
        var sut = CreateSut();
        var originalDirectory = Directory.GetCurrentDirectory();
        var workingDirectory = Directory.CreateDirectory(Path.Combine(_root, "workspace"));

        try
        {
            Directory.SetCurrentDirectory(workingDirectory.FullName);

            var result = sut.ResolveRootPath(null);

            result.FullName.Should().Be(workingDirectory.FullName);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDirectory);
        }
    }

    [Fact]
    public void CreateContext_LoadsNearestDotEnvWithoutOverwritingExistingEnvironmentVariables()
    {
        var parent = Directory.CreateDirectory(Path.Combine(_root, "parent"));
        var child = Directory.CreateDirectory(Path.Combine(parent.FullName, "child"));
        File.WriteAllText(Path.Combine(parent.FullName, ".env"), """
            CodeMeridian_Project=DotEnvProject
            CodeMeridian_Url=http://dotenv
            CodeMeridian_Auth_ApiKey=dotenv-key
            """);
        File.WriteAllText(Path.Combine(child.FullName, "meridian.json"), """
            {
              "project": "LocalProject",
              "codeMeridianUrl": "http://local",
              "allowRepoScripts": true
            }
            """);

        var originalDirectory = Directory.GetCurrentDirectory();
        var originalProject = Environment.GetEnvironmentVariable("CodeMeridian_Project");
        var originalUrl = Environment.GetEnvironmentVariable("CodeMeridian_Url");
        var originalApiKey = Environment.GetEnvironmentVariable("CodeMeridian_Auth_ApiKey");

        try
        {
            Directory.SetCurrentDirectory(child.FullName);
            Environment.SetEnvironmentVariable("CodeMeridian_Project", "ExistingProject");
            Environment.SetEnvironmentVariable("CodeMeridian_Url", null);
            Environment.SetEnvironmentVariable("CodeMeridian_Auth_ApiKey", null);

            var sut = CreateSut();
            var context = sut.CreateContext(child.FullName);

            context.RootPath.FullName.Should().Be(child.FullName);
            context.LocalConfig.Should().NotBeNull();
            context.LocalConfig!.Project.Should().Be("LocalProject");
            context.EnvironmentProject.Should().Be("ExistingProject");
            context.EnvironmentUrl.Should().Be("http://dotenv");
            context.ApiKey.Should().Be("dotenv-key");
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDirectory);
            Environment.SetEnvironmentVariable("CodeMeridian_Project", originalProject);
            Environment.SetEnvironmentVariable("CodeMeridian_Url", originalUrl);
            Environment.SetEnvironmentVariable("CodeMeridian_Auth_ApiKey", originalApiKey);
        }
    }

    private ToolConfigurationService CreateSut(string projectName = "DiscoveredProject", string defaultUrl = "http://default")
    {
        return new ToolConfigurationService(
            new CodeMeridianConfigFileStore(),
            new StubProjectDiscoveryService(projectName),
            Options.Create(new ToolCliDefaults
            {
                DefaultCodeMeridianUrl = defaultUrl
            }));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private sealed class StubProjectDiscoveryService(string projectName) : IProjectDiscoveryService
    {
        public bool ContainsFile(DirectoryInfo root, params string[] extensions) => throw new NotSupportedException();

        public IReadOnlyList<DirectoryInfo> FindTypeScriptRoots(DirectoryInfo root) => throw new NotSupportedException();

        public DirectoryInfo? FindRepositoryRoot(DirectoryInfo start) => throw new NotSupportedException();

        public string ResolveProjectName(DirectoryInfo root) => projectName;
    }
}
