using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CodeMeridian.RoslynIndexer.Pipeline;
using CodeMeridian.Sdk;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace CodeMeridian.RoslynIndexer.Tests.Pipeline;

public sealed class CSharpSemanticModelEnrichmentTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "csharp-semantic-enrichment-tests",
        Guid.NewGuid().ToString("N"));

    public CSharpSemanticModelEnrichmentTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task IndexAsync_UsesCrossFileSemanticTypesForPartialMembersAndInferredLocals()
    {
        var domain = WriteFile("src/Domain.cs", """
            namespace Sample;

            public sealed class Repository
            {
                public void Save() { }
            }

            public sealed class RepositoryFactory
            {
                public Repository Create() => new();
            }
            """);
        var state = WriteFile("src/Service.State.cs", """
            namespace Sample;

            public sealed partial class Service
            {
                private RepositoryFactory Factory { get; } = new();
            }
            """);
        var behavior = WriteFile("src/Service.cs", """
            namespace Sample;

            public sealed partial class Service
            {
                public void Run()
                {
                    var repository = Factory.Create();
                    repository.Save();
                }
            }
            """);
        var handler = new RecordingHandler();
        var client = new CodeMeridianClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost") });
        var sut = new CSharpIndexer(client, NullLogger<CSharpIndexer>.Instance);

        var stats = await sut.IndexAsync([behavior, state, domain], "SampleProject", _root);

        handler.HasCall("Sample.Service::Run()", "Sample.RepositoryFactory::Create()").Should().BeTrue();
        handler.HasCall("Sample.Service::Run()", "Sample.Repository::Save()").Should().BeTrue();
        var saveCall = handler.FindCall("Sample.Service::Run()", "Sample.Repository::Save()");
        saveCall.Should().NotBeNull();
        saveCall!.Value.GetProperty("properties").GetProperty("receiverEvidenceSource").GetString()
            .Should().Be("semantic-model-instance");
        saveCall.Value.GetProperty("properties").GetProperty("semanticTargetDeclaringTypeHint").GetString()
            .Should().Be("Sample.Repository");
        stats.CallResolution.Indeterminate.Should().Be(0);
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
        private readonly List<JsonElement> _edges = [];

        public bool HasCall(string sourceId, string targetId) => FindCall(sourceId, targetId) is not null;

        public JsonElement? FindCall(string sourceId, string targetId) => _edges
            .Cast<JsonElement?>()
            .FirstOrDefault(edge =>
                edge!.Value.GetProperty("type").GetString() == "Calls"
                && edge.Value.GetProperty("sourceId").GetString() is { } source
                && source.EndsWith(sourceId, StringComparison.Ordinal)
                && edge.Value.GetProperty("targetId").GetString() is { } target
                && target.EndsWith(targetId, StringComparison.Ordinal));

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Content is not null)
            {
                using var document = JsonDocument.Parse(await request.Content.ReadAsStringAsync(cancellationToken));
                var path = request.RequestUri!.AbsolutePath;
                if (path.Contains("/nodes/edges", StringComparison.Ordinal))
                {
                    if (document.RootElement.ValueKind == JsonValueKind.Array)
                        _edges.AddRange(document.RootElement.EnumerateArray().Select(item => item.Clone()));
                    else
                        _edges.Add(document.RootElement.Clone());
                }
            }

            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = JsonContent.Create(new { })
            };
        }
    }
}
