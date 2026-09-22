using System.Text.Json;
using CodeMeridian.Core.CodeGraph;
using FluentAssertions;

namespace CodeMeridian.Core.Tests.CodeGraph;

public sealed class CodeEdgeEvidenceTests
{
    [Fact]
    public void Evidence_SerializesWithStableLowerCaseKindAndLocation()
    {
        var edge = NewEdge() with
        {
            EvidenceKind = EdgeEvidenceKind.Extracted,
            EvidenceReason = "roslyn_symbol",
            Resolver = "roslyn.semantic",
            SourceFilePath = "src/Worker.cs",
            SourceLine = 12,
            SourceColumn = 3,
            SourceEndLine = 12,
            SourceEndColumn = 15,
            EvidenceDetails = new() { ["receiverType"] = "Worker" }
        };

        edge.ValidateEvidence().Should().BeNull();
        var json = JsonSerializer.Serialize(edge);
        json.Should().Contain("\"EvidenceKind\":\"extracted\"");
        var read = JsonSerializer.Deserialize<CodeEdge>(json);
        read.Should().BeEquivalentTo(edge);
    }

    [Theory]
    [InlineData("bad reason", "roslyn.semantic")]
    [InlineData("roslyn_symbol", "too long with spaces")]
    public void Evidence_RejectsInvalidCodes(string reason, string resolver)
    {
        (NewEdge() with { EvidenceReason = reason, Resolver = resolver }).ValidateEvidence().Should().NotBeNull();
    }

    [Fact]
    public void Evidence_RejectsReversedAndOversizedValues()
    {
        (NewEdge() with { SourceLine = 9, SourceEndLine = 8 }).ValidateEvidence().Should().NotBeNull();
        (NewEdge() with { EvidenceDetails = Enumerable.Range(0, 9)
            .ToDictionary(index => $"key{index}", _ => "value") }).ValidateEvidence().Should().NotBeNull();
        (NewEdge() with { SourceColumn = 1 }).ValidateEvidence().Should().NotBeNull();
    }

    private static CodeEdge NewEdge() => new()
    {
        SourceId = "a", TargetId = "b", Type = CodeEdgeType.Calls
    };
}
