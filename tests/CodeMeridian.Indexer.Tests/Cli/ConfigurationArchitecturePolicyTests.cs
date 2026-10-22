using System.Net;
using CodeMeridian.Application.Services;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Indexer.Cli.Configuration;
using FluentAssertions;

namespace CodeMeridian.Indexer.Tests.Cli;

public sealed class ConfigurationArchitecturePolicyTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "codemeridian-policy-tests", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("{}")]
    [InlineData("{ invalid }")]
    [InlineData("{\"customValidators\":[\"anything\"]}")]
    public async Task InvalidPolicyStopsBeforeClearingOrUploading(string policy)
    {
        WritePolicy(policy);
        var handler = new RecordingHandler();

        var exitCode = await Run(handler);

        exitCode.Should().Be(2);
        handler.RequestCount.Should().Be(0);
    }

    [Theory]
    [InlineData("custom-policy.json")]
    [InlineData("../outside.json")]
    public async Task MissingOrUnsafeCustomPolicyDoesNotUseDefaultRules(string path)
    {
        Directory.CreateDirectory(root);
        var handler = new RecordingHandler();

        (await Run(handler, path)).Should().Be(2);

        handler.RequestCount.Should().Be(0);
    }

    [Fact]
    public async Task ValidPolicyAllowsTheExistingConfigurationIndexPass()
    {
        WritePolicy("""
            { "layers": [ { "id": "A", "namespaceContainsAny": [".A"] },
                          { "id": "B", "namespaceContainsAny": [".B"] } ],
              "forbiddenDependencies": [ { "from": "A", "to": "B" } ] }
            """);
        var handler = new RecordingHandler();

        (await Run(handler)).Should().Be(0);

        handler.RequestCount.Should().BeGreaterThan(0);
    }

    private Task<int> Run(RecordingHandler handler, string? path = null) => new ConfigurationIndexer().RunAsync(
        new DirectoryInfo(root), "Fixture", "http://localhost", null, new FileRoleClassifier(), null, path,
        clearExistingConfiguration: true, messageHandler: handler);

    private void WritePolicy(string policy)
    {
        Directory.CreateDirectory(Path.Combine(root, ".meridian"));
        File.WriteAllText(Path.Combine(root, ".meridian", "architecture.json"), policy);
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent("{}") });
        }
    }

    private sealed class FileRoleClassifier : IIndexedFileRoleClassifier
    {
        public IndexedFileRole Classify(string relativePath) => IndexedFileRole.Source;
    }
}
