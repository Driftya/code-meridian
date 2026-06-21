using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CodeMeridian.Application.Services;
using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Indexer.Cli.Configuration;
using FluentAssertions;

namespace CodeMeridian.Indexer.Tests.Cli;

public sealed class ConfigurationIndexerTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "codemeridian-configuration-indexer-tests",
        Guid.NewGuid().ToString("N"));

    public ConfigurationIndexerTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task RunAsync_UsesBulkEndpointsAndLogsConfigurationProgress()
    {
        WriteFile(
            "appsettings.json",
            """
            {
              "ConnectionStrings": {
                "Main": "Server=primary;"
              }
            }
            """);
        WriteFile(
            "appsettings.Development.json",
            """
            {
              "ConnectionStrings": {
                "Main": "Server=override;"
              }
            }
            """);

        var handler = new RecordingHandler();
        var sut = new ConfigurationIndexer();

        var output = await CaptureConsoleAsync(() => sut.RunAsync(
            new DirectoryInfo(_root),
            "CodeMeridian",
            "http://localhost",
            apiKey: null,
            fileRoleClassifier: new TestFileRoleClassifier(),
            configurationFilePatterns: null,
            architecturePath: null,
            clearExistingConfiguration: false,
            messageHandler: handler));

        output.Should().Contain("Indexing configuration batch");
        output.Should().Contain("Batch size: 2 file(s)");
        output.Should().Contain("Processed 2/2 configuration files");
        output.Should().Contain("Found 6 nodes, 4 edges");
        output.Should().Contain("Ingesting configuration nodes batch 1/1");
        output.Should().Contain("Uploaded 6/6 configuration nodes");
        output.Should().Contain("Ingesting configuration edges batch 1/1");
        output.Should().Contain("Uploaded 4/4 configuration edges");

        handler.Batches.Should().ContainSingle(batch => batch.Path == "/api/v1/knowledge/nodes/bulk");
        handler.Batches.Should().ContainSingle(batch => batch.Path == "/api/v1/knowledge/nodes/edges/bulk");
        handler.Batches.Should().NotContain(batch => batch.Path == "/api/v1/knowledge/nodes");
        handler.Batches.Should().NotContain(batch => batch.Path == "/api/v1/knowledge/nodes/edges");

        handler.FlattenedRequests.Should().Contain(request =>
            request.Path == "/api/v1/knowledge/nodes/bulk"
            && request.Body.GetProperty("id").GetString() == "CodeMeridian::ConfigurationFile::appsettings.json");
        handler.FlattenedRequests.Should().Contain(request =>
            request.Path == "/api/v1/knowledge/nodes/edges/bulk"
            && request.Body.GetProperty("type").GetString() == "OverridesConfig"
            && request.Body.GetProperty("sourceId").GetString()!.StartsWith("CodeMeridian::ConfigurationEntry::appsettings.Development.json::", StringComparison.Ordinal)
            && request.Body.GetProperty("targetId").GetString() == "CodeMeridian::ConfigurationKey::ConnectionStrings:Main");
    }

    [Fact]
    public async Task RunAsync_SkipsInvalidConfigurationFilesAndContinues()
    {
        WriteFile(
            "appsettings.json",
            """
            {
              "FeatureFlags": {
                "NewDashboard": true
              }
            }
            """);
        WriteFile(
            "meridian.sample.json",
            """
            {
              invalid
            }
            """);

        var handler = new RecordingHandler();
        var sut = new ConfigurationIndexer();

        var output = await CaptureConsoleAsync(() => sut.RunAsync(
            new DirectoryInfo(_root),
            "CodeMeridian",
            "http://localhost",
            apiKey: null,
            fileRoleClassifier: new TestFileRoleClassifier(),
            configurationFilePatterns: null,
            architecturePath: null,
            clearExistingConfiguration: false,
            messageHandler: handler));

        output.Should().Contain("warn: skipped config file 'meridian.sample.json'");
        output.Should().Contain("Found 3 nodes, 2 edges");
        handler.Batches.Should().ContainSingle(batch => batch.Path == "/api/v1/knowledge/nodes/bulk");
        handler.Batches.Should().ContainSingle(batch => batch.Path == "/api/v1/knowledge/nodes/edges/bulk");
    }

    [Fact]
    public async Task RunAsync_WhenBulkNodeIngestFails_StopsBeforeEdgeUpload()
    {
        WriteFile(
            "appsettings.json",
            """
            {
              "FeatureFlags": {
                "NewDashboard": true
              }
            }
            """);

        var handler = new RecordingHandler("/api/v1/knowledge/nodes/bulk");
        var sut = new ConfigurationIndexer();

        Func<Task> act = async () => await sut.RunAsync(
            new DirectoryInfo(_root),
            "CodeMeridian",
            "http://localhost",
            apiKey: null,
            fileRoleClassifier: new TestFileRoleClassifier(),
            configurationFilePatterns: null,
            architecturePath: null,
            clearExistingConfiguration: false,
            messageHandler: handler);

        await act.Should().ThrowAsync<HttpRequestException>();
        handler.Batches.Should().ContainSingle(batch => batch.Path == "/api/v1/knowledge/nodes/bulk");
        handler.Batches.Should().NotContain(batch => batch.Path == "/api/v1/knowledge/nodes/edges/bulk");
    }

    private FileInfo WriteFile(string relativePath, string content)
    {
        var file = new FileInfo(Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        file.Directory!.Create();
        File.WriteAllText(file.FullName, content);
        return file;
    }

    private static async Task<string> CaptureConsoleAsync(Func<Task<int>> action)
    {
        var original = Console.Out;
        using var writer = new StringWriter();
        Console.SetOut(writer);

        try
        {
            await action();
            return writer.ToString();
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private sealed class RecordingHandler(string? failPath = null) : HttpMessageHandler
    {
        public List<(string Method, string Path, JsonElement Body)> FlattenedRequests { get; } = [];
        public List<(string Method, string Path, int ItemCount)> Batches { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var body = request.Content is null ? "{}" : await request.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(body);

            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                var items = doc.RootElement.EnumerateArray().Select(item => item.Clone()).ToArray();
                Batches.Add((request.Method.Method, path, items.Length));
                foreach (var item in items)
                    FlattenedRequests.Add((request.Method.Method, path, item));
            }
            else
            {
                Batches.Add((request.Method.Method, path, 1));
                FlattenedRequests.Add((request.Method.Method, path, doc.RootElement.Clone()));
            }

            if (string.Equals(path, failPath, StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = JsonContent.Create(new { error = "boom" })
                };
            }

            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = JsonContent.Create(new { })
            };
        }
    }

    private sealed class TestFileRoleClassifier : IIndexedFileRoleClassifier
    {
        public IndexedFileRole Classify(string relativePath) => IndexedFileRole.Source;
    }
}
