using CodeMeridian.RoslynIndexer.Pipeline;
using FluentAssertions;

namespace CodeMeridian.RoslynIndexer.Tests.Pipeline;

public sealed class CSharpCallableDefinitionAuditTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"callable-audit-{Guid.NewGuid():N}"));

    [Theory]
    [InlineData("", 0)]
    [InlineData("class Example {}", 0)]
    [InlineData("interface IExample { void Run(); }", 1)]
    [InlineData("class Example { Example() {} void Run() { void Local() {} } }", 3)]
    [InlineData("class Example { int this[int i] => i; }", 1)]
    [InlineData("class Example { public static Example operator +(Example x, Example y) => x; public static implicit operator int(Example x) => 0; }", 2)]
    public void SupportedDeclarationsAreMatchedByExactSpanEvenOnTheSameLine(string source, int expected)
    {
        var (summary, samples) = Audit(source);

        summary.Should().Be(new CSharpCallableDefinitionSummary(expected, expected, 0, 0));
        summary.HasValidAccounting.Should().BeTrue();
        summary.HasGaps.Should().BeFalse();
        samples.Should().BeEmpty();
    }

    [Theory]
    [InlineData("class Example { ~Example() {} }", 1, 0)]
    [InlineData("class Example { int Value { get; set; } }", 2, 0)]
    [InlineData("class Example { int Value => 1; }", 1, 0)]
    [InlineData("class Example { void Run() { System.Action action = () => {}; } }", 2, 1)]
    [InlineData("class Example { void Run() { System.Action action = delegate {}; } }", 2, 1)]
    [InlineData("class Example(int value) {}", 1, 0)]
    [InlineData("struct Example(int value) {}", 1, 0)]
    [InlineData("record Example(int Value);", 1, 0)]
    [InlineData("record struct Example(int Value);", 1, 0)]
    [InlineData("System.Console.WriteLine(); System.Console.WriteLine();", 1, 0)]
    [InlineData("void Local() {} Local();", 2, 1)]
    public void UnrepresentedCallableSyntaxIsReportedExplicitly(string source, int candidates, int represented)
    {
        var (summary, samples) = Audit(source);

        summary.Should().Be(new CSharpCallableDefinitionSummary(candidates, represented, candidates - represented, 0));
        summary.HasValidAccounting.Should().BeTrue();
        summary.HasGaps.Should().BeTrue();
        samples.Should().HaveCount(candidates - represented).And.OnlyContain(sample =>
            sample.FilePath == "Example.cs" && sample.Stage == "definitions"
            && sample.Code == "unrepresented_callable" && sample.LineNumber == 1);
    }

    [Theory]
    [InlineData("class Example { void Run() {} void Run<T>() {} }", 2)]
    [InlineData("class Example { void Run(int value) {} void Run(ref int value) {} }", 2)]
    [InlineData("interface IA { void Run(); } interface IB { void Run(); } class Example : IA, IB { void IA.Run() {} void IB.Run() {} }", 2)]
    [InlineData("class Example { public static implicit operator int(Example x) => 0; public static implicit operator string(Example x) => null; }", 2)]
    public void DistinctSymbolsSharingAnEmittedIdentityAreCoverageGaps(string source, int conflicts)
    {
        var (summary, samples) = Audit(source);

        summary.Unrepresented.Should().Be(0);
        summary.IdentityConflictDeclarations.Should().Be(conflicts);
        summary.HasGaps.Should().BeTrue();
        samples.Should().HaveCount(conflicts).And.OnlyContain(sample => sample.Code == "callable_identity_conflict");
    }

    [Fact]
    public void PartialMethodPartsAcrossFilesCanShareAnIdentity()
    {
        var (summary, samples) = Audit(
            "partial class Example { partial void Run(); }",
            "partial class Example { partial void Run() {} }");

        summary.Should().Be(new CSharpCallableDefinitionSummary(2, 2, 0, 0));
        samples.Should().BeEmpty();
    }

    [Fact]
    public void OrdinaryOverloadsHaveDistinctIdentities()
    {
        var (summary, samples) = Audit("class Example { void Run(int value) {} void Run(string value) {} }");

        summary.Should().Be(new CSharpCallableDefinitionSummary(2, 2, 0, 0));
        samples.Should().BeEmpty();
    }

    [Fact]
    public void SamplesAreBoundedWithoutLosingTotals()
    {
        var (summary, samples) = Audit("class Example {" + string.Join('\n',
            Enumerable.Range(0, 20).Select(index => $"int Value{index} => {index};")) + "}");

        summary.Unrepresented.Should().Be(20);
        samples.Should().HaveCount(8);
    }

    [Fact]
    public void CancellationIsNotConvertedIntoAnAuditResult()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var action = () => CSharpCallableDefinitionAuditor.Analyze([], [], cancellation.Token);

        action.Should().Throw<OperationCanceledException>();
    }

    private (CSharpCallableDefinitionSummary Summary, IReadOnlyList<CSharpAnalysisDiagnostic> Samples) Audit(params string[] sources)
    {
        var paths = sources.Select((_, index) => index == 0 ? "Example.cs" : $"Example{index}.cs").ToArray();
        var catalog = CSharpSemanticModelCatalog.Create(paths.Select((path, index) =>
                new KeyValuePair<string, string>(Path.Combine(_root.FullName, path), sources[index]))
            .ToDictionary());
        var files = paths.Select(path => (Path: path, File: catalog.Find(new FileInfo(Path.Combine(_root.FullName, path)))!)).ToArray();
        var nodes = new List<IngestNodeRequest>();
        foreach (var (path, file) in files)
            new CSharpAstWalker(path, "Example", nodes, [], file.SemanticModel).Visit(file.Root);
        return CSharpCallableDefinitionAuditor.Analyze(files, nodes, CancellationToken.None);
    }

    public void Dispose() => _root.Delete(recursive: true);
}
