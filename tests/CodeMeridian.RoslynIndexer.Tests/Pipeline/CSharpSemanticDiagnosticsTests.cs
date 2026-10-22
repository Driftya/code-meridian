using CodeMeridian.RoslynIndexer.Pipeline;
using FluentAssertions;
using Microsoft.CodeAnalysis;

namespace CodeMeridian.RoslynIndexer.Tests.Pipeline;

public sealed class CSharpSemanticDiagnosticsTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"semantic-diagnostics-{Guid.NewGuid():N}"));

    [Fact]
    public void CompilationReportsDeclarationAndBodyErrorsWithoutCountingParseErrorsAgain()
    {
        var root = _root.FullName;
        var broken = new FileInfo(Path.Combine(root, "Broken.cs"));
        var catalog = CSharpSemanticModelCatalog.Create(new Dictionary<string, string>
        {
            [broken.FullName] = "class Broken { void Run() { int value = ; } }",
            [Path.Combine(root, "Unknown.cs")] = "class Unknown { Missing field; void Run() { DoesNotExist(); } }"
        });
        var parseCodes = catalog.Find(broken)!.Root.GetDiagnostics().Where(item => item.Severity == DiagnosticSeverity.Error)
            .Select(item => item.Id).ToArray();

        var semanticErrors = catalog.GetSemanticDiagnostics(CancellationToken.None)
            .Where(item => item.Severity == DiagnosticSeverity.Error).ToArray();

        parseCodes.Should().NotBeEmpty();
        semanticErrors.Select(item => item.Id).Should().Contain("CS0246").And.Contain("CS0103")
            .And.NotIntersectWith(parseCodes);
    }

    [Fact]
    public void CompilationDiagnosticsHonorCancellation()
    {
        var catalog = CSharpSemanticModelCatalog.Create(new Dictionary<string, string>
        {
            [Path.Combine(_root.FullName, "Example.cs")] = "class Example { Missing field; }"
        });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var action = () => catalog.GetSemanticDiagnostics(cancellation.Token).ToArray();

        action.Should().Throw<OperationCanceledException>();
    }

    [Fact]
    public void LocationFreeErrorsRemainBoundedAndDoNotExposeDiagnosticMessages()
    {
        var builder = new CSharpAnalysisReportBuilder(Path.GetTempPath(), 0, 0);
        var error = Diagnostic.Create(new DiagnosticDescriptor("TEST001", "Compiler issue", "private diagnostic text",
            "compiler", DiagnosticSeverity.Error, isEnabledByDefault: true), Location.None);
        builder.Semantic(Enumerable.Repeat(error, 20));
        var empty = new RelationshipResolutionCollector("Calls").Build();

        var report = builder.Build(empty, empty);

        report.SemanticErrors.Should().Be(20);
        report.DiagnosticSamples.Should().HaveCount(8).And.OnlyContain(sample =>
            sample.FilePath == "." && sample.Stage == "semantic" && sample.Code == "TEST001" && sample.LineNumber == null);
        report.Coverage.Should().Be("partial");
        report.ExitCode.Should().Be(0);
    }

    public void Dispose() => _root.Delete(recursive: true);
}
