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
            && request.Body.GetProperty("properties").GetProperty("accessPattern").GetString() == "Configure"
            && request.Body.GetProperty("properties").GetProperty("optionsType").GetString() == "Neo4jOptions");
    }

    [Fact]
    public async Task IndexAsync_ResolvesSectionNameConstantsAndGetBindings()
    {
        var optionsFile = WriteFile(
            "src/EmbeddingOptions.cs",
            """
            namespace Demo;

            public sealed class EmbeddingOptions
            {
                public const string SectionName = "Embedding";
            }
            """);
        var dependencyInjectionFile = WriteFile(
            "src/DependencyInjection.cs",
            """
            namespace Demo;

            public static class DependencyInjection
            {
                public static void Add(IServiceCollection services, Microsoft.Extensions.Configuration.IConfiguration configuration)
                {
                    services.Configure<EmbeddingOptions>(configuration.GetSection(EmbeddingOptions.SectionName));
                    var options = configuration.GetSection(EmbeddingOptions.SectionName).Get<EmbeddingOptions>() ?? new();
                }
            }
            """);

        var handler = new RecordingHandler();
        var client = new CodeMeridianClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost") });
        var sut = new CSharpIndexer(client, NullLogger<CSharpIndexer>.Instance);

        await sut.IndexAsync([optionsFile, dependencyInjectionFile], "CodeMeridian", _root);

        handler.Requests.Should().Contain(request =>
            request.Path == "/api/v1/knowledge/nodes/edges"
            && request.Body.GetProperty("type").GetString() == "BindsConfig"
            && request.Body.GetProperty("targetId").GetString() == "CodeMeridian::ConfigurationKey::Embedding"
            && request.Body.GetProperty("properties").GetProperty("optionsType").GetString() == "EmbeddingOptions");

        handler.Requests.Should().Contain(request =>
            request.Path == "/api/v1/knowledge/nodes/edges"
            && request.Body.GetProperty("type").GetString() == "BindsConfig"
            && request.Body.GetProperty("properties").GetProperty("accessPattern").GetString() == "Get"
            && request.Body.GetProperty("targetId").GetString() == "CodeMeridian::ConfigurationKey::Embedding");
    }

    [Fact]
    public async Task IndexAsync_ResolvesGetRequiredSectionBindings()
    {
        var optionsFile = WriteFile(
            "src/StorageOptions.cs",
            """
            namespace Demo;

            public sealed class StorageOptions
            {
            }
            """);
        var dependencyInjectionFile = WriteFile(
            "src/DependencyInjection.cs",
            """
            namespace Demo;

            public static class DependencyInjection
            {
                public static void Add(IServiceCollection services, Microsoft.Extensions.Configuration.IConfiguration configuration)
                {
                    services.Configure<StorageOptions>(configuration.GetRequiredSection("Storage"));
                }
            }
            """);

        var handler = new RecordingHandler();
        var client = new CodeMeridianClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost") });
        var sut = new CSharpIndexer(client, NullLogger<CSharpIndexer>.Instance);

        await sut.IndexAsync([optionsFile, dependencyInjectionFile], "CodeMeridian", _root);

        handler.Requests.Should().Contain(request =>
            request.Path == "/api/v1/knowledge/nodes/edges"
            && request.Body.GetProperty("type").GetString() == "BindsConfig"
            && request.Body.GetProperty("sourceId").GetString() == "CodeMeridian::Method::Demo.Add(IServiceCollection,Microsoft.Extensions.Configuration.IConfiguration)"
            && request.Body.GetProperty("targetId").GetString() == "CodeMeridian::ConfigurationKey::Storage"
            && HasEdgeProperty(request.Body, "accessPattern", "Configure")
            && HasEdgeProperty(request.Body, "optionsType", "StorageOptions"));
    }

    [Fact]
    public async Task IndexAsync_ResolvesInterpolatedSectionNamesFromCodeMeridianSolutionPattern()
    {
        var optionsFile = WriteFile(
            "src/KeywordEnrichmentOptions.cs",
            """
            namespace Demo;

            public sealed class KeywordEnrichmentOptions
            {
                public const string SectionName = "KeywordEnrichment";
            }
            """);
        var dependencyInjectionFile = WriteFile(
            "src/ApplicationDependencyInjection.cs",
            """
            namespace Demo;

            public static class DependencyInjection
            {
                public static void Add(IServiceCollection services, Microsoft.Extensions.Configuration.IConfiguration configuration)
                {
                    services.Configure<KeywordEnrichmentOptions>(configuration.GetSection($"CodeMeridian:{KeywordEnrichmentOptions.SectionName}"));
                }
            }
            """);

        var handler = new RecordingHandler();
        var client = new CodeMeridianClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost") });
        var sut = new CSharpIndexer(client, NullLogger<CSharpIndexer>.Instance);

        await sut.IndexAsync([optionsFile, dependencyInjectionFile], "CodeMeridian", _root);

        handler.Requests.Should().Contain(request =>
            request.Path == "/api/v1/knowledge/nodes/edges"
            && request.Body.GetProperty("type").GetString() == "BindsConfig"
            && request.Body.GetProperty("targetId").GetString() == "CodeMeridian::ConfigurationKey::CodeMeridian:KeywordEnrichment");
    }

    [Fact]
    public async Task IndexAsync_OnRealCodeMeridianDependencyInjectionFiles_ExtractsExpectedTypedConfigEdges()
    {
        var repoRoot = FindRepositoryRoot();
        var files = new[]
        {
            new FileInfo(Path.Combine(repoRoot, "src", "Infrastructure", "DependencyInjection.cs")),
            new FileInfo(Path.Combine(repoRoot, "src", "Infrastructure", "Configuration", "Neo4jOptions.cs")),
            new FileInfo(Path.Combine(repoRoot, "src", "Core", "Knowledge", "EmbeddingOptions.cs")),
            new FileInfo(Path.Combine(repoRoot, "src", "Application", "DependencyInjection.cs")),
            new FileInfo(Path.Combine(repoRoot, "src", "Application", "Services", "KeywordEnrichmentOptions.cs")),
            new FileInfo(Path.Combine(repoRoot, "src", "Application", "Services", "KeywordClassificationOptions.cs")),
        };

        var handler = new RecordingHandler();
        var client = new CodeMeridianClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost") });
        var sut = new CSharpIndexer(client, NullLogger<CSharpIndexer>.Instance);

        await sut.IndexAsync(files, "CodeMeridian", repoRoot);

        handler.Requests.Should().Contain(request =>
            request.Path == "/api/v1/knowledge/nodes/edges"
            && request.Body.GetProperty("type").GetString() == "BindsConfig"
            && request.Body.GetProperty("targetId").GetString() == "CodeMeridian::ConfigurationKey::Neo4j");
        handler.Requests.Should().Contain(request =>
            request.Path == "/api/v1/knowledge/nodes/edges"
            && request.Body.GetProperty("type").GetString() == "BindsConfig"
            && request.Body.GetProperty("targetId").GetString() == "CodeMeridian::ConfigurationKey::Embedding");
        handler.Requests.Should().Contain(request =>
            request.Path == "/api/v1/knowledge/nodes/edges"
            && request.Body.GetProperty("type").GetString() == "BindsConfig"
            && request.Body.GetProperty("targetId").GetString() == "CodeMeridian::ConfigurationKey::Embedding"
            && request.Body.GetProperty("properties").GetProperty("accessPattern").GetString() == "Get"
            && request.Body.GetProperty("properties").GetProperty("optionsType").GetString() == "EmbeddingOptions");
        handler.Requests.Should().Contain(request =>
            request.Path == "/api/v1/knowledge/nodes/edges"
            && request.Body.GetProperty("type").GetString() == "BindsConfig"
            && request.Body.GetProperty("targetId").GetString() == "CodeMeridian::ConfigurationKey::CodeMeridian:KeywordEnrichment");
        handler.Requests.Should().Contain(request =>
            request.Path == "/api/v1/knowledge/nodes/edges"
            && request.Body.GetProperty("type").GetString() == "BindsConfig"
            && request.Body.GetProperty("targetId").GetString() == "CodeMeridian::ConfigurationKey::CodeMeridian:KeywordClassification");
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

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "CodeMeridian.sln")))
                return current.FullName;

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root from the test runtime directory.");
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

    private static bool HasEdgeProperty(JsonElement body, string name, string expectedValue)
    {
        if (!body.TryGetProperty("properties", out var properties))
            return false;

        return properties.TryGetProperty(name, out var propertyValue)
            && string.Equals(propertyValue.GetString(), expectedValue, StringComparison.Ordinal);
    }
}
