using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CodeMeridian.RoslynIndexer.Pipeline;
using CodeMeridian.Sdk;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace CodeMeridian.RoslynIndexer.Tests.Pipeline;

public sealed class CSharpIncrementalIndexerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "csharp-incremental-indexer-tests", Guid.NewGuid().ToString("N"));

    public CSharpIncrementalIndexerTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task IndexAsync_WhenOnlyCallerIsIngested_ResolvesCallAgainstUnchangedCallee()
    {
        var caller = WriteFile("src/Caller.cs", """
            namespace Sample;
            public sealed class Caller
            {
                public void Run() => new Callee().Execute();
            }
            """);
        var callee = WriteFile("src/Callee.cs", """
            namespace Sample;
            public sealed class Callee
            {
                public void Execute(string mode = "safe") { }
            }
            """);
        var (sut, handler) = CreateSut();

        var stats = await sut.IndexAsync([caller], "SampleProject", _root, resolutionFiles: [caller, callee]);

        handler.HasEdge("Calls", "Sample.Caller::Run()", "Sample.Callee::Execute(string)").Should().BeTrue();
        handler.HasNode("Sample.Callee::Execute(string)").Should().BeFalse("unchanged nodes must not be upserted");
        stats.ScannedFiles.Should().Be(2);
        stats.IngestedFiles.Should().Be(1);
        stats.ResolvedCallEdges.Should().Be(1);
    }

    [Fact]
    public async Task IndexAsync_WhenOnlyCalleeIsIngested_RecreatesIncomingEdgeFromUnchangedCaller()
    {
        var caller = WriteFile("src/Caller.cs", """
            namespace Sample;
            public sealed class Caller
            {
                public void Run() => new Callee().Execute();
            }
            """);
        var callee = WriteFile("src/Callee.cs", """
            namespace Sample;
            public sealed class Callee
            {
                public void Execute() { }
            }
            """);
        var (sut, handler) = CreateSut();

        await sut.IndexAsync([callee], "SampleProject", _root, resolutionFiles: [caller, callee]);

        handler.HasEdge("Calls", "Sample.Caller::Run()", "Sample.Callee::Execute()").Should().BeTrue();
        handler.HasNode("Sample.Caller::Run()").Should().BeFalse("unchanged source nodes must not be upserted");
    }

    [Fact]
    public async Task IndexAsync_WhenOnlyImplementationIsIngested_RecreatesCrossFileTypeRelationships()
    {
        var contract = WriteFile("src/IWorker.cs", """
            namespace Sample;
            public interface IWorker { void Execute(); }
            """);
        var implementation = WriteFile("src/Worker.cs", """
            namespace Sample;
            public sealed class Worker : IWorker { public void Execute() { } }
            """);
        var consumer = WriteFile("src/Consumer.cs", """
            namespace Sample;
            public sealed class Consumer(IWorker worker) { public void Run() => worker.Execute(); }
            """);
        var (sut, handler) = CreateSut();

        await sut.IndexAsync(
            [implementation],
            "SampleProject",
            _root,
            resolutionFiles: [contract, implementation, consumer]);

        handler.HasEdge("Implements", "Sample.Worker", "Sample.IWorker").Should().BeTrue();
        handler.HasEdge("Uses", "Sample.Consumer", "Sample.IWorker").Should().BeTrue();
        handler.HasEdge("Calls", "Sample.Consumer::Run()", "Sample.IWorker::Execute()").Should().BeTrue();
    }

    [Fact]
    public async Task IndexAsync_ReportsDeterministicUnresolvedReasons()
    {
        var caller = WriteFile("src/Caller.cs", """
            namespace Sample;
            public sealed class Caller
            {
                public void Run() => Missing();
            }
            """);
        var (sut, _) = CreateSut();

        var stats = await sut.IndexAsync([caller], "SampleProject", _root);

        stats.AttemptedCallEdges.Should().Be(1);
        stats.ResolvedCallEdges.Should().Be(0);
        stats.UnresolvedEdgesByReason.Should().ContainKey("missing_target").WhoseValue.Should().Be(1);
    }

    [Fact]
    public async Task IndexAsync_WhenCalleeWasDeleted_DoesNotEmitAnEdgeToTheMissingTarget()
    {
        var caller = WriteFile("src/Caller.cs", """
            namespace Sample;
            public sealed class Caller
            {
                public void Run() => new Callee().Execute();
            }
            """);
        var (sut, handler) = CreateSut();

        var stats = await sut.IndexAsync(
            Array.Empty<FileInfo>(),
            "SampleProject",
            _root,
            resolutionFiles: [caller],
            isIncremental: true);

        handler.HasAnyEdgeTo("Sample.Callee::Execute()").Should().BeFalse();
        stats.UnresolvedEdgesByReason.Should().ContainKey("missing_target");
        stats.IngestedFiles.Should().Be(0);
    }

    [Fact]
    public async Task IndexAsync_WhenCalleeWasRenamed_DoesNotRetainTheOldCallEdge()
    {
        var caller = WriteFile("src/Caller.cs", """
            namespace Sample;
            public sealed class Caller
            {
                public void Run() => new Callee().Execute();
            }
            """);
        var callee = WriteFile("src/Callee.cs", """
            namespace Sample;
            public sealed class Callee
            {
                public void ExecuteRenamed() { }
            }
            """);
        var (sut, handler) = CreateSut();

        var stats = await sut.IndexAsync(
            [callee],
            "SampleProject",
            _root,
            resolutionFiles: [caller, callee],
            isIncremental: true);

        handler.HasAnyEdgeTo("Sample.Callee::Execute()").Should().BeFalse();
        handler.HasEdge("Calls", "Sample.Caller::Run()", "Sample.Callee::ExecuteRenamed()").Should().BeFalse();
        stats.UnresolvedEdgesByReason.Should().ContainKey("missing_target");
    }

    [Fact]
    public async Task IndexAsync_RepeatedIncrementalPasses_EmitStableCallEdgesAndRunNodeIds()
    {
        var caller = WriteFile("src/Caller.cs", """
            namespace Sample;
            public sealed class Caller
            {
                public void Run() => new Callee().Execute();
            }
            """);
        var callee = WriteFile("src/Callee.cs", """
            namespace Sample;
            public sealed class Callee { public void Execute() { } }
            """);
        var (sut, handler) = CreateSut();

        var first = await sut.IndexAsync([caller], "SampleProject", _root, resolutionFiles: [caller, callee], isIncremental: true);
        var firstCalls = handler.CountEdges("Calls");
        var firstRunIds = handler.NodeIds.Where(id => id.Contains("::IndexRun::", StringComparison.Ordinal)).ToArray();
        handler.Clear();

        var second = await sut.IndexAsync([caller], "SampleProject", _root, resolutionFiles: [caller, callee], isIncremental: true);

        handler.CountEdges("Calls").Should().Be(firstCalls);
        second.ResolvedCallEdges.Should().Be(first.ResolvedCallEdges);
        handler.HasIndexRunDiagnostic().Should().BeTrue("index metadata must remain compatible with older servers");
        handler.NodeIds.Where(id => id.Contains("::IndexRun::", StringComparison.Ordinal))
            .Should().Equal(firstRunIds);
    }

    private (CSharpIndexer Sut, RecordingHandler Handler) CreateSut()
    {
        var handler = new RecordingHandler();
        var client = new CodeMeridianClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost") });
        return (new CSharpIndexer(client, NullLogger<CSharpIndexer>.Instance), handler);
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
        public List<(string Path, JsonElement Body)> Requests { get; } = [];

        public bool HasNode(string id) => Requests.Any(request =>
            request.Path == "/api/v1/knowledge/nodes" &&
            request.Body.GetProperty("id").GetString() is { } actual && actual.EndsWith(id, StringComparison.Ordinal));

        public bool HasEdge(string type, string sourceId, string targetId) => Requests.Any(request =>
            request.Path == "/api/v1/knowledge/nodes/edges" &&
            request.Body.GetProperty("type").GetString() == type &&
            request.Body.GetProperty("sourceId").GetString() is { } actualSource && actualSource.EndsWith(sourceId, StringComparison.Ordinal) &&
            request.Body.GetProperty("targetId").GetString() is { } actualTarget && actualTarget.EndsWith(targetId, StringComparison.Ordinal));

        public IEnumerable<string> NodeIds => Requests
            .Where(request => request.Path == "/api/v1/knowledge/nodes")
            .Select(request => request.Body.GetProperty("id").GetString())
            .OfType<string>();

        public int CountEdges(string type) => Requests.Count(request =>
            request.Path == "/api/v1/knowledge/nodes/edges" &&
            request.Body.GetProperty("type").GetString() == type);

        public bool HasAnyEdgeTo(string targetId) => Requests.Any(request =>
            request.Path == "/api/v1/knowledge/nodes/edges" &&
            request.Body.GetProperty("targetId").GetString() is { } actualTarget &&
            actualTarget.EndsWith(targetId, StringComparison.Ordinal));

        public bool HasIndexRunDiagnostic() => Requests.Any(request =>
            request.Path == "/api/v1/knowledge/nodes" &&
            request.Body.GetProperty("type").GetString() == "Diagnostic" &&
            request.Body.GetProperty("properties").GetProperty("externalKind").GetString() == "IndexRun");

        public void Clear() => Requests.Clear();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "{}" : await request.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(body);
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/bulk", StringComparison.Ordinal))
                path = path[..^"/bulk".Length];
            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in document.RootElement.EnumerateArray())
                    Requests.Add((path, item.Clone()));
            }
            else
            {
                Requests.Add((path, document.RootElement.Clone()));
            }

            return new HttpResponseMessage(HttpStatusCode.Created) { Content = JsonContent.Create(new { }) };
        }
    }
}
