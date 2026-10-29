using System.Net;
using System.Net.Http.Json;
using CodeMeridian.Application.Services;
using CodeMeridian.Core.Projects;
using CodeMeridian.Indexer.Cli.Commands;
using CodeMeridian.Indexer.Cli.Configuration;
using CodeMeridian.Tooling.Discovery;
using CodeMeridian.Tooling.Storage;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace CodeMeridian.Indexer.Tests.Cli;

public sealed class ConfigurationProducerEvidenceTests : IDisposable
{
    private readonly DirectoryInfo root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "config-evidence-" + Guid.NewGuid().ToString("N")));

    [Theory]
    [InlineData("{\"Enabled\":true}", false, 0, "unknown")]
    [InlineData("{\"Enabled\":true}", true, 0, "unknown")]
    [InlineData("{invalid", false, 2, "partial")]
    [InlineData("{\"Enabled\":true,\"Enabled\":false}", false, 2, "partial")]
    public async Task DefinitionsHaveVerifiedReadsButNeverBorrowUsageOrRuleReadiness(string content, bool clear, int exit, string coverage)
    {
        File.WriteAllText(Path.Combine(root.FullName, "appsettings.json"), content);
        ProjectProfileSnapshot? profile = null;
        var outcomes = new List<ProjectAnalyzerOutcomeSnapshot>();
        var handler = new Respond(async request =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/v1/project-profiles/") profile = await request.Content!.ReadFromJsonAsync<ProjectProfileSnapshot>();
            if (request.RequestUri.AbsolutePath.EndsWith("/outcomes")) outcomes.Add((await request.Content!.ReadFromJsonAsync<ProjectAnalyzerOutcomeSnapshot>())!);
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new { generation = 1 }) };
        });
        var settings = new ResolvedIndexerSettings { RootPath = root, Project = "Fixture", CodeMeridianUrl = "http://fixture",
            StorageMode = IndexerStorageMode.Repository, IncludeDocs = false, SkipCSharp = true, SkipTypeScript = true };
        var reporter = new IndexProfileRunReporter(new(new RepositoryInventoryDiscovery()))
        { HttpClientFactory = (_, _) => new HttpClient(handler, false) { BaseAddress = new("http://fixture") } };

        var result = await reporter.RunAsync(settings, report => report!.RunAnalyzerAsync("configuration", ["."],
            () => new ConfigurationIndexer().RunAsync(root, "Fixture", "http://fixture", null,
                new ConfiguredIndexedFileRoleClassifier(Options.Create(new CodebaseIndexingOptions())), null, null, clear, messageHandler: handler, profileRun: report), true));

        result.Should().Be(exit);
        outcomes.Last().Outcomes.Single().Coverage.Should().Be(coverage);
        var configuration = profile!.Profile.Analyzers.Single(analyzer => analyzer.Id == "configuration");
        configuration.SupportedCapabilities.Should().Equal("configuration_definitions");
        configuration.Limitations.Should().Contain(item => item.Contains("No architecture policy was observed"));
        outcomes.Last().Outcomes.Single().Limitations.Should().Contain(item => item.Contains("do not authorize"));
    }

    [Theory]
    [InlineData("app.config", "<configuration />")]
    [InlineData(".env", "not an assignment")]
    [InlineData("settings.yaml", "a: &cycle\n  b: *cycle")]
    public void UnsupportedFormatsAndMalformedOrRecursiveInputsAreNotExecutedEmptyDefinitions(string name, string content)
    {
        var file = new FileInfo(Path.Combine(root.FullName, name));
        ConfigurationFileParser.TryParseContent(file, root.FullName, content, out var entries, out _).Should().BeFalse();
        entries.Should().BeEmpty();
    }

    public void Dispose() => root.Delete(true);
    private sealed class Respond(Func<HttpRequestMessage, Task<HttpResponseMessage>> response) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => response(request); }
}
