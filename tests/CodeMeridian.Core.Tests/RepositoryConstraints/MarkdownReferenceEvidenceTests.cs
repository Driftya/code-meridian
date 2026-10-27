using System.Text.Json;
using CodeMeridian.Core.Projects;
using CodeMeridian.Core.RepositoryConstraints;
using FluentAssertions;

namespace CodeMeridian.Core.Tests.RepositoryConstraints;

public sealed class MarkdownReferenceEvidenceTests
{
    private static readonly string Hash = "sha256:" + new string('a', 64);

    [Fact]
    public void CompleteEmptyFileFactsRoundTripAndLegacySnapshotsOmitEvidence()
    {
        var snapshot = Snapshot();
        snapshot.Validate().Should().BeNull();
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var restored = JsonSerializer.Deserialize<ProjectProfileSnapshot>(JsonSerializer.Serialize(snapshot, options), options)!;
        restored.Validate().Should().BeNull();
        restored.MarkdownEvidence.Should().BeEquivalentTo(snapshot.MarkdownEvidence);
        JsonSerializer.Serialize(snapshot with { MarkdownEvidence = null }, options).Should().NotContain("markdownEvidence");
    }

    [Theory]
    [InlineData("source")]
    [InlineData("parser")]
    [InlineData("version")]
    [InlineData("status")]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("generation")]
    [InlineData("project")]
    [InlineData("path")]
    [InlineData("identity")]
    [InlineData("document-parser")]
    [InlineData("text")]
    [InlineData("document-version")]
    [InlineData("document-status")]
    [InlineData("diagnostics")]
    [InlineData("normalization")]
    [InlineData("span")]
    public void UnboundMalformedOrFalselyCompleteFactsAreRejected(string change)
    {
        var snapshot = Snapshot();
        var evidence = snapshot.MarkdownEvidence!;
        var document = evidence.Documents[0];
        evidence = change switch
        {
            "source" => evidence with { SourceFingerprint = "sha256:" + new string('b', 64) },
            "parser" => evidence with { ParserFingerprint = "bad" },
            "version" => evidence with { ContractVersion = "2.0" },
            "status" => evidence with { Status = "ready" },
            "missing" => evidence with { Documents = [] },
            "duplicate" => evidence with { Documents = [document, document] },
            _ => evidence with { Documents = [change switch
            {
                "generation" => document with { Generation = 2 },
                "project" => document with { Project = "Other" },
                "path" => document with { SourcePath = "../a.md" },
                "identity" => document with { FileId = "Docs::doc::a.md::part1" },
                "document-parser" => document with { ParserFingerprint = "sha256:" + new string('b', 64) },
                "text" => document with { TextFingerprint = "bad" },
                "document-version" => document with { ContractVersion = "2.0" },
                "document-status" => document with { Status = "incomplete" },
                "diagnostics" => document with { Diagnostics = [new("a.md", "parse_failed", "Parsing failed.")] },
                "normalization" => document with { References = [Fact() with { NormalizedPath = "elsewhere.md" }] },
                _ => document with { References = [Fact() with { TargetSpan = new(0, 99, 1, 1) }] }
            }] }
        };
        (snapshot with { MarkdownEvidence = evidence }).Validate().Should().NotBeNull();
    }

    [Fact]
    public void IncompleteEvidenceCanDescribeMissingAndFailedParsesWithoutClaimingCompleteness()
    {
        var snapshot = Snapshot();
        var evidence = snapshot.MarkdownEvidence!;
        (snapshot with { MarkdownEvidence = evidence with { Status = "incomplete", Documents = [] } }).Validate().Should().BeNull();
        var document = evidence.Documents[0] with { Status = "incomplete", Diagnostics = [new("a.md", "parse_failed", "Parsing failed.")] };
        (snapshot with { MarkdownEvidence = evidence with { Status = "incomplete", Documents = [document] } }).Validate().Should().BeNull();
    }

    [Fact]
    public void EvidenceCannotCoverNonMarkdownInventoryOrExceedProjectBounds()
    {
        var snapshot = Snapshot();
        var evidence = snapshot.MarkdownEvidence!;
        var text = snapshot with { Files = [new("a.md", "text")], Profile = snapshot.Profile with { FileKinds = [new("text", 1, ["a.md"])] } };
        text.Validate().Should().NotBeNull();
        (snapshot with { MarkdownEvidence = evidence with { Documents = Enumerable.Repeat(evidence.Documents[0], 10001).ToArray() } })
            .Validate().Should().NotBeNull();
        var longTarget = new string('a', 2000);
        var fact = Fact() with { RawTarget = longTarget, Target = longTarget, NormalizedPath = longTarget, TargetSpan = new(0, 2000, 1, 1) };
        (snapshot with { MarkdownEvidence = evidence with { Documents = [evidence.Documents[0] with { References = Enumerable.Repeat(fact, 251).ToArray() }] } })
            .Validate().Should().Contain("limit");
    }

    private static MarkdownReferenceFact Fact() => new("link", new(0, 9, 1, 1), new(4, 4, 1, 5), "b.md", "b.md", "local", "b.md", null);

    private static ProjectProfileSnapshot Snapshot() => new(1,
        new("Docs", DateTimeOffset.UtcNow, "complete", 1, [new("markdown", 1, ["a.md"])], [], 0, [], [], 0)
            { SourceFingerprint = Hash }, [new("a.md", "markdown")])
    {
        MarkdownEvidence = new(Hash, Hash, "complete", [new("Docs", "Docs::doc::a.md", "a.md", 1, Hash, Hash, "complete", [], [])])
    };
}
