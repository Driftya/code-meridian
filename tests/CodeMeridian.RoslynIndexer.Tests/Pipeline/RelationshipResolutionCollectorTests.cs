using CodeMeridian.RoslynIndexer.Pipeline;
using FluentAssertions;

namespace CodeMeridian.RoslynIndexer.Tests.Pipeline;

public sealed class RelationshipResolutionCollectorTests
{
    [Fact]
    public void Build_SelectsBoundedDeterministicSamplesAcrossFileRolesAndReceiverKinds()
    {
        var candidates = new[]
        {
            Candidate("tests/ZetaTests.cs", "Test", "UnknownMember", 40),
            Candidate("src/Zeta.cs", "Source", "UnknownMember", 30),
            Candidate("src/Alpha.cs", "Source", "Chained", 20),
            Candidate("generated/Mapper.g.cs", "Generated", "UnknownMember", 10)
        };

        var forward = Build(candidates);
        var reverse = Build(candidates.Reverse());

        forward.Should().Equal(reverse);
        forward.Should().HaveCount(3);
        forward.Select(sample => sample.FileRole).Should().Equal("Source", "Test", "Generated");
        forward.Should().OnlyContain(sample => sample.Reason == "unknown_member_receiver");
        forward.Should().OnlyContain(sample => sample.TargetName == "Run");
    }

    private static RelationshipResolutionSample[] Build(IEnumerable<SampleCandidate> candidates)
    {
        var collector = new RelationshipResolutionCollector("Calls");
        foreach (var candidate in candidates)
        {
            var sourceId = $"Project::Method::{candidate.FilePath}::Execute()";
            collector.Record(
                RelationshipResolutionDisposition.Indeterminate,
                "unknown_member_receiver",
                new IngestNodeRequest(
                    sourceId,
                    "Execute()",
                    "Method",
                    "Demo",
                    candidate.FilePath,
                    candidate.LineNumber,
                    null,
                    Properties: new() { ["fileRole"] = candidate.FileRole }),
                new IngestEdgeRequest(
                    sourceId,
                    string.Empty,
                    "Calls",
                    CallName: "Run",
                    ParamCount: 0,
                    Properties: new() { ["receiverKind"] = candidate.ReceiverKind }));
        }

        return collector.Build().Samples.ToArray();
    }

    private static SampleCandidate Candidate(
        string filePath,
        string fileRole,
        string receiverKind,
        int lineNumber) =>
        new(filePath, fileRole, receiverKind, lineNumber);

    private sealed record SampleCandidate(
        string FilePath,
        string FileRole,
        string ReceiverKind,
        int LineNumber);
}
