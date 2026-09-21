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

    [Fact]
    public async Task IndexAsync_UsesRestoredPackageCompileReferencesForExternalCalls()
    {
        WriteFile("Sample.csproj", """
            <Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><ImplicitUsings>enable</ImplicitUsings></PropertyGroup></Project>
            """);
        var packageFolder = Path.Combine(_root, "packages");
        var compileFolder = Directory.CreateDirectory(Path.Combine(packageFolder, "probe", "test", "lib"));
        var library = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create("PackageProbe",
            [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText("""
                namespace PackageProbe;
                public static class Factory { public static Client Create() => new Client(); }
                public class Client { public void Finish() {} }
                """)],
            [Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary));
        using (var output = File.Create(Path.Combine(compileFolder.FullName, "PackageProbe.dll")))
            library.Emit(output).Success.Should().BeTrue();
        WriteFile("obj/project.assets.json", JsonSerializer.Serialize(new
        {
            targets = new Dictionary<string, object>
            {
                ["net10.0"] = new Dictionary<string, object>
                {
                    ["Probe/test"] = new { compile = new Dictionary<string, object> { ["lib/PackageProbe.dll"] = new { } } }
                }
            },
            libraries = new Dictionary<string, object> { ["Probe/test"] = new { type = "package", path = "probe/test" } },
            packageFolders = new Dictionary<string, object> { [packageFolder] = new { } }
        }));
        var file = WriteFile("Service.cs", """
            public class Service { public void Run() { PackageProbe.Factory.Create().Finish(); } }
            """);
        var handler = new RecordingHandler();
        var client = new CodeMeridianClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost") });
        var stats = await new CSharpIndexer(client, NullLogger<CSharpIndexer>.Instance).IndexAsync([file], "SampleProject", _root);

        stats.CallResolution.Reasons.Should().Contain("external_or_unindexed:semantic_external_target", 2);
        stats.CallResolution.Indeterminate.Should().Be(0);
    }

    [Fact]
    public async Task IndexAsync_KeepsSameNamedNestedTypesAndTheirMethodsDistinct()
    {
        var file = WriteFile("Nested.cs", """
            namespace Sample;
            public class First
            {
                private class Handler { public void Save() {} }
                public void Run() { new Handler().Save(); }
            }
            public class Second
            {
                private class Handler { public void Save() {} }
                public void Run() { new Handler().Save(); }
            }
            """);
        var handler = new RecordingHandler();
        var client = new CodeMeridianClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost") });
        var stats = await new CSharpIndexer(client, NullLogger<CSharpIndexer>.Instance).IndexAsync([file], "SampleProject", _root);

        handler.HasCall("Sample.First::Run()", "Sample.First.Handler::Save()").Should().BeTrue();
        handler.HasCall("Sample.Second::Run()", "Sample.Second.Handler::Save()").Should().BeTrue();
        stats.CallResolution.UnresolvedLocal.Should().Be(0);
    }

    [Fact]
    public async Task IndexAsync_HonorsUniformSdkImplicitUsingsForAwaitedReceivers()
    {
        WriteFile("Sample.csproj", """
            <Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><ImplicitUsings>enable</ImplicitUsings></PropertyGroup></Project>
            """);
        var file = WriteFile("Service.cs", """
            namespace Sample;
            public class Repository { public void Save() {} }
            public class Service
            {
                public Task<Repository> Create() => Task.FromResult(new Repository());
                public async Task Run() { var repository = await Create(); repository.Save(); }
            }
            """);
        var handler = new RecordingHandler();
        var client = new CodeMeridianClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost") });
        await new CSharpIndexer(client, NullLogger<CSharpIndexer>.Instance).IndexAsync([file], "SampleProject", _root);

        handler.HasCall("Sample.Service::Run()", "Sample.Repository::Save()").Should().BeTrue();
    }

    [Fact]
    public async Task IndexAsync_UsesBoundOverloadInsteadOfAmbiguousNameAndArity()
    {
        var file = WriteFile("Service.cs", """
            namespace Sample;
            public class Service
            {
                public void Save(string value) { } public void Save(int value) { }
                public void Run() { Save(42); }
            }
            """);
        var handler = new RecordingHandler();
        var client = new CodeMeridianClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost") });

        var stats = await new CSharpIndexer(client, NullLogger<CSharpIndexer>.Instance)
            .IndexAsync([file], "SampleProject", _root);

        handler.HasCall("Sample.Service::Run()", "Sample.Service::Save(int)").Should().BeTrue();
        handler.HasCall("Sample.Service::Run()", "Sample.Service::Save(string)").Should().BeFalse();
        stats.CallResolution.UnresolvedLocal.Should().Be(0);
    }

    [Fact]
    public async Task IndexAsync_DoesNotMapBoundExternalMethodToLocalNameCollision()
    {
        var file = WriteFile("Service.cs", """
            using static System.GC;
            namespace Sample;
            public class Service { public void Run() { Collect(); } }
            public class Unrelated { public void Collect() { } }
            """);
        var handler = new RecordingHandler();
        var client = new CodeMeridianClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost") });

        var stats = await new CSharpIndexer(client, NullLogger<CSharpIndexer>.Instance)
            .IndexAsync([file], "SampleProject", _root);

        handler.HasCall("Sample.Service::Run()", "Sample.Unrelated::Collect()").Should().BeFalse();
        stats.CallResolution.ExternalOrUnindexed.Should().Be(1);
        stats.CallResolution.UnresolvedLocal.Should().Be(0);
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
