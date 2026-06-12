using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CodeMeridian.RoslynIndexer.Pipeline;
using CodeMeridian.Sdk;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace CodeMeridian.RoslynIndexer.Tests.Pipeline;

public sealed class CSharpConfigurationUsageExtractorTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "codemeridian-csharp-config-tests",
        Guid.NewGuid().ToString("N"));

    public CSharpConfigurationUsageExtractorTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task IndexAsync_ExtractsDirectConfigurationReads()
    {
        var file = WriteFile(
            "src/SettingsReader.cs",
            """
            namespace Demo;

            public sealed class SettingsReader
            {
                public string Read(Microsoft.Extensions.Configuration.IConfiguration configuration) =>
                    configuration["CodeMeridian:Auth:ApiKey"] ?? string.Empty;
            }
            """);

        var handler = new RecordingHandler();
        var client = new CodeMeridianClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost") });
        var sut = new CSharpIndexer(client, NullLogger<CSharpIndexer>.Instance);

        await sut.IndexAsync([file], "CodeMeridian", _root);

        handler.Requests.Should().Contain(request =>
            request.Path == "/api/v1/knowledge/nodes"
            && request.Body.GetProperty("id").GetString() == "CodeMeridian::ConfigurationKey::CodeMeridian:Auth:ApiKey"
            && request.Body.GetProperty("type").GetString() == "ConfigurationKey");

        handler.Requests.Should().Contain(request =>
            request.Path == "/api/v1/knowledge/nodes/edges"
            && request.Body.GetProperty("type").GetString() == "ReadsConfig"
            && request.Body.GetProperty("sourceId").GetString() == "CodeMeridian::Method::Demo.Read(Microsoft.Extensions.Configuration.IConfiguration)"
            && request.Body.GetProperty("targetId").GetString() == "CodeMeridian::ConfigurationKey::CodeMeridian:Auth:ApiKey"
            && request.Body.GetProperty("properties").GetProperty("rawKey").GetString() == "CodeMeridian:Auth:ApiKey");
    }

    [Fact]
    public async Task IndexAsync_ExtractsTypedConfigurationBindings()
    {
        var file = WriteFile(
            "src/Bootstrap.cs",
            """
            namespace Demo;

            public sealed class Neo4jOptions
            {
            }

            public static class Bootstrap
            {
                public static void Add(IServiceCollection services, Microsoft.Extensions.Configuration.IConfiguration configuration)
                {
                    services.Configure<Neo4jOptions>(configuration.GetSection("Neo4j"));
                }
            }
            """);

        var handler = new RecordingHandler();
        var client = new CodeMeridianClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost") });
        var sut = new CSharpIndexer(client, NullLogger<CSharpIndexer>.Instance);

        await sut.IndexAsync([file], "CodeMeridian", _root);

        handler.Requests.Should().Contain(request =>
            request.Path == "/api/v1/knowledge/nodes/edges"
            && request.Body.GetProperty("type").GetString() == "BindsConfig"
            && request.Body.GetProperty("sourceId").GetString() == "CodeMeridian::Method::Demo.Add(IServiceCollection,Microsoft.Extensions.Configuration.IConfiguration)"
            && request.Body.GetProperty("targetId").GetString() == "CodeMeridian::ConfigurationKey::Neo4j"
            && request.Body.GetProperty("properties").GetProperty("optionsType").GetString() == "Neo4jOptions");
    }

    private FileInfo WriteFile(string relativePath, string content)
    {
        var file = new FileInfo(Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        file.Directory!.Create();
        File.WriteAllText(file.FullName, content);
        return file;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<(string Method, string Path, JsonElement Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "{}" : await request.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(body);
            Requests.Add((request.Method.Method, request.RequestUri!.AbsolutePath, doc.RootElement.Clone()));

            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = JsonContent.Create(new { })
            };
        }
    }
}
