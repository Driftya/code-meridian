using CodeMeridian.Application.Services;
using CodeMeridian.Core.KeywordGraph;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace CodeMeridian.Application.Tests.Services;

public sealed class DefaultKeywordExtractionServiceTests
{
    [Fact]
    public void Extract_SplitsCommonCodeNamingStyles_AndRoutes()
    {
        var sut = Build();
        var source = new KeywordSourceNode
        {
            Id = "Method:CodeMeridian.KeywordGraph.BuildMinimalContextAsync",
            Kind = "CodeNode",
            TextBySource = new Dictionary<string, string?>
            {
                ["name"] = "BuildMinimalContextAsync",
                ["summary"] = "Handles find_stale_knowledge and /api/notes/unread-replies/count."
            }
        };

        var result = sut.Extract(source);

        result.Keywords.Select(static keyword => keyword.NormalizedValue).Should().Contain([
            "build",
            "minimal",
            "context",
            "find",
            "stale",
            "knowledge",
            "notes",
            "unread",
            "replies",
            "count"
        ]);
    }

    [Fact]
    public void Extract_RejectsShortWordsAndStopwords_ButKeepsAllowedShortTerms()
    {
        var sut = Build(new KeywordEnrichmentOptions
        {
            MinimumKeywordLength = 4,
            AllowedShortTerms = ["api", "sdk", "ts"]
        });
        var source = new KeywordSourceNode
        {
            Id = "doc-1",
            Kind = "KnowledgeDocument",
            TextBySource = new Dictionary<string, string?>
            {
                ["content"] = "Use the new API SDK for TS clients and set the token."
            }
        };

        var result = sut.Extract(source);
        var keywords = result.Keywords.Select(static keyword => keyword.NormalizedValue).ToArray();

        keywords.Should().Contain(["api", "sdk", "clients", "token", "ts"]);
        keywords.Should().NotContain(["new", "for", "the", "use", "set"]);
    }

    [Fact]
    public void Extract_UsesAdditionalStopwordsFromConfiguration()
    {
        var sut = Build(new KeywordEnrichmentOptions
        {
            AdditionalStopwords = ["meridian", "driftya"]
        });
        var source = new KeywordSourceNode
        {
            Id = "doc-2",
            Kind = "KnowledgeDocument",
            TextBySource = new Dictionary<string, string?>
            {
                ["content"] = "CodeMeridian by Driftya indexes authentication routes."
            }
        };

        var result = sut.Extract(source);

        result.Keywords.Select(static keyword => keyword.NormalizedValue).Should().Contain(["code", "indexes", "authentication", "routes"]);
        result.Keywords.Select(static keyword => keyword.NormalizedValue).Should().NotContain(["meridian", "driftya"]);
    }

    [Fact]
    public void Extract_RejectsGuidLikeAndNumericNoise()
    {
        var sut = Build();
        var source = new KeywordSourceNode
        {
            Id = "diagnostic-1",
            Kind = "Diagnostic",
            TextBySource = new Dictionary<string, string?>
            {
                ["summary"] = "Error 1234 on 550e8400-e29b-41d4-a716-446655440000 while validating PaymentGateway."
            }
        };

        var result = sut.Extract(source);
        var keywords = result.Keywords.Select(static keyword => keyword.NormalizedValue).ToArray();

        keywords.Should().Contain(["error", "validating", "payment", "gateway"]);
        keywords.Should().NotContain(["1234", "550e8400", "e29b", "41d4", "a716", "446655440000"]);
    }

    private static DefaultKeywordExtractionService Build(KeywordEnrichmentOptions? options = null) =>
        new(Options.Create(options ?? new KeywordEnrichmentOptions()));
}
