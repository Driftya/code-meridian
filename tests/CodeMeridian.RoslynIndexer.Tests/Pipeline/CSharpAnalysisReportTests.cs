using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CodeMeridian.RoslynIndexer.Pipeline;
using CodeMeridian.Sdk;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace CodeMeridian.RoslynIndexer.Tests.Pipeline;

public sealed class CSharpAnalysisReportTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"csharp-analysis-{Guid.NewGuid():N}"));
    private readonly Handler _handler = new();

    [Theory]
    [InlineData("")]
    [InlineData("class Example { void Run() {} }")]
    public async Task SuccessfulExtractionWithNoDependenciesHasExplicitUnauditedCoverage(string source)
    {
        var stats = await Indexer().IndexAsync([Write("Example.cs", source)], "Example", _root.FullName);

        stats.AttemptedCallEdges.Should().Be(0);
        stats.AttemptedReferenceEdges.Should().Be(0);
        stats.Analysis.Should().NotBeNull();
        stats.Analysis!.Should().Match<CSharpAnalysisReport>(report => report.RequestedFiles == 1
            && report.ReadFiles == 1 && report.ExtractedFiles == 1 && report.FailedFiles == 0 && report.ParseErrors == 0);
        stats.Analysis.ExitCode.Should().Be(0);
        stats.Analysis.Coverage.Should().Be("unknown");
        stats.Analysis.ProjectModel.Should().Be("synthetic_compilation");
        stats.Analysis.SemanticErrors.Should().Be(0);
        stats.Analysis.CallRelationships.Should().NotBeNull();
        stats.Analysis.CallRelationships!.Attempted.Should().Be(0);
        stats.Analysis.CallRelationships.HasValidAccounting.Should().BeTrue();
        stats.Analysis.TypeRelationships.Should().NotBeNull();
        stats.Analysis.TypeRelationships!.Attempted.Should().Be(0);
        stats.Analysis.CallableDefinitions!.HasGaps.Should().BeFalse();
        stats.Analysis.ContractVersion.Should().Be("1.2");
        _handler.Reports.Should().ContainSingle().Which.GetProperty("Coverage").GetString().Should().Be("unknown");
    }

    [Fact]
    public async Task MissingFileIsReportedWithoutDiscardingHealthyFiles()
    {
        var good = Write("Good.cs", "class Good {}");
        var missing = new FileInfo(Path.Combine(_root.FullName, "Missing.cs"));

        var stats = await Indexer().IndexAsync([missing, good], "Example", _root.FullName);

        stats.Analysis!.ReadFiles.Should().Be(1);
        stats.Analysis.ExtractedFiles.Should().Be(1);
        stats.Analysis.FailedFiles.Should().Be(1);
        stats.Analysis.Coverage.Should().Be("partial");
        stats.Analysis.ExitCode.Should().Be(2);
        stats.Analysis.DiagnosticSamples.Should().ContainSingle().Which
            .Should().Be(new CSharpAnalysisDiagnostic("Missing.cs", "read", "FileNotFoundException", null));
    }

    [Fact]
    public async Task SyntaxErrorsAreBoundedAndIncludeCodesAndLocations()
    {
        var source = string.Join('\n', Enumerable.Repeat("class {", 20));

        var stats = await Indexer().IndexAsync([Write("Broken.cs", source)], "Example", _root.FullName);

        stats.Analysis!.ParseErrors.Should().BeGreaterThan(8);
        stats.Analysis.DiagnosticSamples.Should().HaveCount(8).And.OnlyContain(diagnostic =>
            diagnostic.FilePath == "Broken.cs" && diagnostic.Stage == "parse"
            && diagnostic.Code.StartsWith("CS") && diagnostic.LineNumber > 0);
        stats.Analysis.Coverage.Should().Be("partial");
        stats.Analysis.ExitCode.Should().Be(2);
    }

    [Fact]
    public async Task SemanticErrorsProducePartialEvidenceWithoutClaimingTheProjectBuildFailed()
    {
        var file = Write("Example.cs", "class Example { Missing field; void Run() { DoesNotExist(); } }");

        var stats = await Indexer().IndexAsync([file], "Example", _root.FullName);

        stats.Analysis!.ParseErrors.Should().Be(0);
        stats.Analysis.SemanticErrors.Should().BeGreaterThan(0);
        stats.Analysis.ExtractedFiles.Should().Be(1);
        stats.Analysis.Coverage.Should().Be("partial");
        stats.Analysis.ExitCode.Should().Be(0);
        stats.Analysis.DiagnosticSamples.Should().Contain(diagnostic => diagnostic.Stage == "semantic"
            && diagnostic.FilePath == "Example.cs" && diagnostic.Code == "CS0246" && diagnostic.LineNumber == 1);
        stats.Analysis.Limitations.Should().Contain(limit => limit.Contains("synthetic compilation"));
        _handler.Reports.Should().ContainSingle().Which.GetProperty("SemanticErrors").GetInt32().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task SemanticDiagnosticsAreBoundedAndContainNoSourceText()
    {
        var source = "class Example {\n" + string.Join('\n', Enumerable.Range(0, 20)
            .Select(index => $"MissingSecretType{index} field{index};")) + "\n}";

        var stats = await Indexer().IndexAsync([Write("Example.cs", source)], "Example", _root.FullName);

        stats.Analysis!.SemanticErrors.Should().Be(20);
        stats.Analysis.DiagnosticSamples.Should().HaveCount(8).And.OnlyContain(diagnostic =>
            diagnostic.Stage == "semantic" && diagnostic.Code == "CS0246" && diagnostic.LineNumber >= 2);
        JsonSerializer.Serialize(stats.Analysis).Should().NotContain("MissingSecretType");
    }

    [Theory]
    [InlineData("class Example { void Run(dynamic receiver) { receiver.Execute(); } }")]
    [InlineData("class Example { void Run(dynamic receiver) { receiver.Execute(); } void Execute() {} }")]
    [InlineData("class Example { void Run() { var receiver = (dynamic)new object(); receiver.Execute(); } }")]
    public async Task IndeterminateCallsProducePartialEvidenceEvenWithoutCompilerErrors(string source)
    {
        var file = Write("Example.cs", source);

        var stats = await Indexer().IndexAsync([file], "Example", _root.FullName);

        stats.Analysis!.SemanticErrors.Should().Be(0);
        stats.Analysis.CallRelationships!.Attempted.Should().Be(1);
        stats.Analysis.CallRelationships.Indeterminate.Should().Be(1);
        stats.Analysis.CallRelationships.HasValidAccounting.Should().BeTrue();
        stats.Analysis.Coverage.Should().Be("partial");
        stats.Analysis.ExitCode.Should().Be(0);
    }

    [Fact]
    public async Task ExternalCallsAndDuplicateLocalCallsHaveExplicitAccountingWithoutClaimingCompleteCoverage()
    {
        var file = Write("Example.cs", "class Example { void Run() { Save(); Save(); System.Console.WriteLine(); } void Save() {} }");

        var stats = await Indexer().IndexAsync([file], "Example", _root.FullName);

        stats.Analysis!.SemanticErrors.Should().Be(0);
        var calls = stats.Analysis.CallRelationships!;
        calls.Attempted.Should().Be(3);
        calls.ResolvedLocal.Should().Be(2);
        calls.ExternalOrUnindexed.Should().Be(1);
        calls.DuplicateEdges.Should().Be(1);
        calls.UniqueResolvedEdges.Should().Be(1);
        calls.HasValidAccounting.Should().BeTrue();
        stats.Analysis.Coverage.Should().Be("unknown");
        _handler.Reports.Single().GetProperty("CallRelationships").GetProperty("Attempted").GetInt32().Should().Be(3);
    }

    [Fact]
    public async Task ExtractionFailureDoesNotPublishPartOfTheFailedFile()
    {
        // Duplicate aliases cause the existing syntax walker's alias map construction to fail.
        var file = Write("Broken.cs", "using Alias = System.String; using Alias = System.Int32; class Broken {}");

        var stats = await Indexer().IndexAsync([file], "Example", _root.FullName);

        stats.Nodes.Should().Be(0);
        stats.Analysis!.ReadFiles.Should().Be(1);
        stats.Analysis.ExtractedFiles.Should().Be(0);
        stats.Analysis.FailedFiles.Should().Be(1);
        stats.Analysis.CallableDefinitions!.Candidates.Should().Be(0);
        stats.Analysis.DiagnosticSamples.Where(diagnostic => diagnostic.Stage == "extract")
            .Should().ContainSingle();
        stats.Analysis.ExitCode.Should().Be(2);
    }

    [Fact]
    public async Task CallableGapsArePersistedWithBoundedPrivateDiagnostics()
    {
        var file = Write("Example.cs", "class Example {" + string.Join('\n',
            Enumerable.Range(0, 20).Select(index => $"int PrivateValue{index} => {index};")) + "}");

        var stats = await Indexer().IndexAsync([file], "Example", _root.FullName);

        stats.Analysis!.CallableDefinitions.Should().Be(new CSharpCallableDefinitionSummary(20, 0, 20, 0));
        stats.Analysis.DiagnosticSamples.Should().HaveCount(8).And.OnlyContain(sample => sample.Stage == "definitions");
        stats.Analysis.Coverage.Should().Be("partial");
        stats.Analysis.ExitCode.Should().Be(0);
        var report = _handler.Reports.Single();
        report.GetProperty("CallableDefinitions").GetProperty("Unrepresented").GetInt32().Should().Be(20);
        report.GetRawText().Should().NotContain("PrivateValue");
    }

    [Fact]
    public async Task ParserAndConstantsConsumeTheSameSingleRead()
    {
        var file = Write("Example.cs", "this is not the supplied source");
        var reads = 0;
        var stats = await Indexer().IndexAsync([file], "Example", _root.FullName,
            readSource: (_, _) =>
            {
                reads++;
                return Task.FromResult("class Example { const string Key = \"Enabled\"; void Run() {} }");
            });

        reads.Should().Be(1);
        stats.Analysis!.ParseErrors.Should().Be(0);
        stats.Analysis.ExtractedFiles.Should().Be(1);
    }

    [Fact]
    public async Task PipelineReportsGeneratedFilesAsSkipped()
    {
        Write("Good.cs", "class Good {}");
        Write("Generated.g.cs", "class Generated {}");
        var pipeline = new IndexerPipeline(Indexer(), Client(), NullLogger<IndexerPipeline>.Instance);

        var stats = await pipeline.RunAsync(_root, "Example", false);

        stats.Analysis!.RequestedFiles.Should().Be(2);
        stats.Analysis.ExtractedFiles.Should().Be(1);
        stats.Analysis.SkippedFiles.Should().Be(1);
        stats.Analysis.Coverage.Should().Be("partial");
        stats.Analysis.ExitCode.Should().Be(0);
    }

    [Fact]
    public async Task CancellationDuringSourceReadIsNotDowngradedToAFileFailure()
    {
        var file = Write("Example.cs", "class Example {}");
        using var cancellation = new CancellationTokenSource();
        var action = () => Indexer().IndexAsync([file], "Example", _root.FullName, cancellation.Token,
            readSource: (_, token) =>
            {
                cancellation.Cancel();
                token.ThrowIfCancellationRequested();
                return Task.FromResult("");
            });

        await action.Should().ThrowAsync<OperationCanceledException>();
        _handler.Reports.Should().BeEmpty();
    }

    [Fact]
    public async Task UploadFailureDoesNotReturnSuccessfulWorkerEvidence()
    {
        _handler.Fail = true;
        var action = () => Indexer().IndexAsync([Write("Example.cs", "class Example {}")], "Example", _root.FullName);

        await action.Should().ThrowAsync<HttpRequestException>();
        _handler.Reports.Should().BeEmpty();
    }

    private CodeMeridianClient Client() => new(new HttpClient(_handler) { BaseAddress = new Uri("http://localhost") });
    private CSharpIndexer Indexer() => new(Client(), NullLogger<CSharpIndexer>.Instance);
    private FileInfo Write(string name, string content)
    {
        var file = new FileInfo(Path.Combine(_root.FullName, name));
        File.WriteAllText(file.FullName, content);
        return file;
    }
    public void Dispose() => _root.Delete(recursive: true);

    private sealed class Handler : HttpMessageHandler
    {
        public bool Fail { get; set; }
        public List<JsonElement> Reports { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Fail) return new(HttpStatusCode.BadRequest);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var nodes = body.RootElement.ValueKind == JsonValueKind.Array
                ? body.RootElement.EnumerateArray().ToArray() : [body.RootElement];
            foreach (var node in nodes)
                if (node.TryGetProperty("properties", out var properties) && properties.ValueKind == JsonValueKind.Object
                    && properties.TryGetProperty("analysisReport", out var report))
                    Reports.Add(JsonDocument.Parse(report.GetString()!).RootElement.Clone());
            return new(HttpStatusCode.Created) { Content = JsonContent.Create(new { }) };
        }
    }
}
