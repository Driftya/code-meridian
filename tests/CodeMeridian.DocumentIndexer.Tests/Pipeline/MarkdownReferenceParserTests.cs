using System.Text.Json;
using CodeMeridian.Core.RepositoryConstraints;
using CodeMeridian.DocumentIndexer.Pipeline;
using FluentAssertions;

namespace CodeMeridian.DocumentIndexer.Tests.Pipeline;

public sealed class MarkdownReferenceParserTests
{
    [Fact]
    public void InlineLinksImagesAndReferenceFormsKeepOccurrenceAndDefinitionSpans()
    {
        const string content = """
            [inline](a.md "title") ![image](images/a.png)
            [full][ref] [collapsed][] [shortcut] ![picture][ref]

            [ref]: <docs/a%20b.md#part> "Title"
            [collapsed]: b.md
            [shortcut]: c.md
            """;
        var document = Parse(content);

        document.Status.Should().Be("complete");
        document.References.Should().HaveCount(6);
        document.References.Select(item => item.Kind).Should().Equal("link", "image", "link", "link", "link", "image");
        document.References.Select(item => item.NormalizedPath).Should().Equal("docs/a.md", "docs/images/a.png",
            "docs/docs/a b.md", "docs/b.md", "docs/c.md", "docs/docs/a b.md");
        foreach (var reference in document.References)
            content.Substring(reference.TargetSpan.Offset, reference.TargetSpan.Length).Should().Be(reference.RawTarget);
        var full = document.References[2];
        full.RawTarget.Should().Be("<docs/a%20b.md#part>");
        full.Fragment.Should().Be("part");
        full.SourceSpan.Line.Should().Be(2);
        full.TargetSpan.Line.Should().Be(4);
        content.Substring(full.SourceSpan.Offset, full.SourceSpan.Length).Should().Be("[full][ref]");
    }

    [Fact]
    public void CodeEscapedSyntaxAndUndefinedReferencesAreLiteralText()
    {
        var document = Parse("""
            `[inline](no.md)`
            ~~~markdown
            [fenced](no.md)
            ~~~

                [indented](no.md)

            \[escaped](no.md) [undefined][absent]
            [valid](yes.md)
            """);
        document.Status.Should().Be("complete");
        document.References.Should().ContainSingle(item => item.Target == "yes.md");
    }

    [Theory]
    [InlineData("[x](a\\(b\\).md)", "a(b).md", "docs/a(b).md")]
    [InlineData("[x](<a b.md>)", "a b.md", "docs/a b.md")]
    [InlineData("[x](a&amp;b.md)", "a&b.md", "docs/a&b.md")]
    [InlineData("[x](a%2Fb.md)", "a%2Fb.md", "docs/a/b.md")]
    [InlineData("[x](/images/å%20b.png)", "/images/å%20b.png", "images/å b.png")]
    public void CommonMarkEscapesEntitiesSpacesAndPercentEncodingAreExplicit(string content, string target, string path)
    {
        var reference = Parse(content).References.Single();
        reference.Target.Should().Be(target);
        reference.NormalizedPath.Should().Be(path);
    }

    [Fact]
    public void HtmlIsExplicitlyUnsupportedAndAutolinksAreExternalWithoutFetching()
    {
        var document = Parse("<a href='missing.md'>html</a> <https://example.invalid/a> <a@example.invalid>\n\n<img src='outside.png'>");
        document.Status.Should().Be("incomplete");
        document.Diagnostics.Should().OnlyContain(item => item.Code == "unsupported_html");
        document.References.Should().HaveCount(2).And.OnlyContain(item => item.TargetKind == "external");
        document.References[1].Target.Should().Be("mailto:a@example.invalid");
    }

    [Theory]
    [InlineData("[empty]()", "fragment_only", "docs/guide.md", null)]
    [InlineData("[fragment](#)", "fragment_only", "docs/guide.md", "")]
    [InlineData("[query](a.md?download)", "unsupported", null, null)]
    [InlineData("[data](data:image/png;base64,foo)", "unsupported", null, null)]
    [InlineData("[outside](../../outside.md)", "outside_repository", null, null)]
    public void UnsupportedAndSameDocumentFormsRetainExplicitFacts(string content, string kind, string? path, string? fragment)
    {
        var document = Parse(content);
        document.Status.Should().Be("complete");
        var reference = document.References.Single();
        reference.TargetKind.Should().Be(kind);
        reference.NormalizedPath.Should().Be(path);
        reference.Fragment.Should().Be(fragment);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\r")]
    public void SourceSpansUseUtf16OffsetsAndOneBasedLinesAndColumns(string newline)
    {
        var content = "😀" + newline + "  [x](a.md)";
        var reference = Parse(content).References.Single();
        reference.SourceSpan.Should().Be(new MarkdownReferenceSpan(2 + newline.Length + 2, 9, 2, 3));
        reference.TargetSpan.Line.Should().Be(2);
        reference.TargetSpan.Column.Should().Be(7);
    }

    [Fact]
    public void FileIdentitySurvivesLineMovesAndGenerationChangesWhileTextIdentityChanges()
    {
        var first = Parse("[x](a.md)");
        var moved = MarkdownReferenceParser.Parse("Docs", "docs/guide.md", "\n[x](a.md)", 8);
        moved.FileId.Should().Be(first.FileId);
        moved.TextFingerprint.Should().NotBe(first.TextFingerprint);
        moved.ParserFingerprint.Should().Be(first.ParserFingerprint).And.StartWith("sha256:").And.HaveLength(71);
        moved.Generation.Should().Be(8);
        Parse("").References.Should().BeEmpty();
        Parse("").Status.Should().Be("complete");
        MarkdownReferenceParser.Parse("Other", "docs/guide.md", "[x](a.md)", 7).FileId.Should().NotBe(first.FileId);
        JsonSerializer.Deserialize<MarkdownReferenceDocument>(JsonSerializer.Serialize(first)).Should().BeEquivalentTo(first);
    }

    [Fact]
    public void ReferenceAndTextBoundsNeverClaimCompleteCoverage()
    {
        var many = Parse(string.Concat(Enumerable.Repeat("[x](a.md) ", 1001)));
        many.Status.Should().Be("incomplete");
        many.References.Should().HaveCount(1000);
        many.Diagnostics.Should().Contain(item => item.Code == "reference_limit");
        Parse(new string('a', 1_000_001)).Diagnostics.Should().Contain(item => item.Code == "document_limit");
        Parse("[x](" + new string('a', 2049) + ")").Diagnostics.Should().Contain(item => item.Code == "target_limit");
    }

    [Fact]
    public void ExcessiveNestingReturnsIncompleteEvidenceWithoutPublishingPartialFacts()
    {
        var result = Parse("[first](a.md)\n\n" + string.Concat(Enumerable.Repeat("> ", 200)) + "[deep](b.md)");
        result.Status.Should().Be("incomplete");
        result.References.Should().BeEmpty();
        result.Diagnostics.Should().Contain(item => item.Code == "parse_failed");
    }

    [Fact]
    public void ParsedFactsResolveAgainstInventoryAndPreserveRuleEvidence()
    {
        var document = Parse("![ok](/images/a%20b.png)\n![missing](missing.png)\n[unverified](#anchor)");
        var inventory = new RepositoryConstraintInventory(7, "complete", true,
            [new("docs/guide.md", "markdown"), new("images/a b.png", "asset")], []) { Project = "Docs" };
        var policy = new RepositoryConstraintPolicy("1.0", [new("docs", ["docs/**"], []), new("images", ["images/**"], [])], [],
            [new("assets", "docs", ["images"], true, ["image"], "Use approved assets.", "error", [])]);
        var result = MarkdownReferenceEvaluator.Evaluate(policy, inventory, document);
        result.Result.Status.Should().Be("evaluated");
        result.References.Select(item => item.Status).Should().Equal("resolved", "missing", "fragment_only");
        result.Result.Rules.Single().Findings.Should().ContainSingle(item => item.Status == "missing" && item.Line == 2 && item.Column == 1);
    }

    [Fact]
    public void CancellationAndUnsafeSourcePathsAreRejected()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var action = () => MarkdownReferenceParser.Parse("Docs", "a.md", "", 7, cancellation.Token);
        action.Should().Throw<OperationCanceledException>();
        var unsafePath = () => MarkdownReferenceParser.Parse("Docs", "../a.md", "", 7);
        unsafePath.Should().Throw<ArgumentException>();
    }

    private static MarkdownReferenceDocument Parse(string content) => MarkdownReferenceParser.Parse("Docs", "docs/guide.md", content, 7);
}
