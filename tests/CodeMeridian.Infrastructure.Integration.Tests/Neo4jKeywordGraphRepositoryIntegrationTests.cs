using CodeMeridian.Core.CodeGraph;
using CodeMeridian.Core.KeywordGraph;
using CodeMeridian.Infrastructure.Configuration;
using CodeMeridian.Infrastructure.Graph;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CodeMeridian.Infrastructure.Integration.Tests;

public sealed class Neo4jKeywordGraphRepositoryIntegrationTests : IAsyncLifetime
{
    private readonly Neo4jOptions _options;
    private Neo4jCodeGraphRepository? _codeRepository;
    private Neo4jKeywordGraphRepository? _keywordRepository;

    public Neo4jKeywordGraphRepositoryIntegrationTests()
    {
        _options = TestEnvironment.TryGetNeo4jOptions()
            ?? throw new InvalidOperationException("Neo4j connection details were not found in environment or repo .env.");
    }

    public async Task InitializeAsync()
    {
        _codeRepository = new Neo4jCodeGraphRepository(Options.Create(_options), NullLogger<Neo4jCodeGraphRepository>.Instance);
        _keywordRepository = new Neo4jKeywordGraphRepository(Options.Create(_options), NullLogger<Neo4jKeywordGraphRepository>.Instance);
        await _codeRepository.InitializeAsync();
        await _keywordRepository.InitializeAsync();
    }

    public async Task DisposeAsync()
    {
        if (_keywordRepository is not null)
            await _keywordRepository.DisposeAsync();

        if (_codeRepository is not null)
            await _codeRepository.DisposeAsync();
    }

    [Fact]
    public async Task ReplaceKeywordsAsync_WithSourceNode_UpsertsKeywordsAndSupportsRelatedLookup()
    {
        var projectContext = $"Integration.KeywordGraph.{Guid.NewGuid():N}";
        var first = CreateNode(
            id: $"{projectContext}.First",
            name: "OrderWorkflow",
            summary: "Processes payment authentication workflows",
            projectContext: projectContext);
        var second = CreateNode(
            id: $"{projectContext}.Second",
            name: "PaymentKnowledge",
            summary: "Authentication workflows and payment retries",
            projectContext: projectContext);

        try
        {
            await _codeRepository!.UpsertNodeAsync(first);
            await _codeRepository.UpsertNodeAsync(second);

            await _keywordRepository!.ReplaceKeywordsAsync(new ReplaceKeywordRelationshipsCommand
            {
                SourceNodeId = first.Id,
                KeywordTextChecksum = "checksum-1",
                EnrichmentVersion = 1,
                Keywords =
                [
                    new ExtractedKeyword
                    {
                        Value = "payment",
                        NormalizedValue = "payment",
                        Count = 2,
                        Weight = 1.0d,
                        Sources = ["name", "summary"]
                    },
                    new ExtractedKeyword
                    {
                        Value = "authentication",
                        NormalizedValue = "authentication",
                        Count = 1,
                        Weight = 0.9d,
                        Sources = ["summary"]
                    }
                ]
            });

            await _keywordRepository.ReplaceKeywordsAsync(new ReplaceKeywordRelationshipsCommand
            {
                SourceNodeId = second.Id,
                KeywordTextChecksum = "checksum-2",
                EnrichmentVersion = 1,
                Keywords =
                [
                    new ExtractedKeyword
                    {
                        Value = "payment",
                        NormalizedValue = "payment",
                        Count = 1,
                        Weight = 0.8d,
                        Sources = ["summary"]
                    },
                    new ExtractedKeyword
                    {
                        Value = "authentication",
                        NormalizedValue = "authentication",
                        Count = 1,
                        Weight = 0.85d,
                        Sources = ["summary"]
                    }
                ]
            });

            await _keywordRepository.RecalculateKeywordStatisticsAsync(projectContext);

            var related = await _keywordRepository.FindRelatedByKeywordsAsync(new KeywordRelatedNodeQuery
            {
                SourceNodeId = first.Id,
                TargetKinds = ["Class"],
                MinimumSharedKeywords = 2,
                MinimumScore = 0.1d,
                MaximumDocumentFrequencyRatio = 1.0d,
                Limit = 10
            });

            related.Should().ContainSingle(match =>
                match.TargetNodeId == second.Id
                && match.SharedKeywordCount >= 2
                && match.MatchedKeywords.Contains("payment")
                && match.MatchedKeywords.Contains("authentication"));
        }
        finally
        {
            await _codeRepository!.DeleteProjectAsync(projectContext);
            await _keywordRepository!.RecalculateKeywordStatisticsAsync(projectContext);
        }
    }

    [Fact]
    public async Task ReplaceKeywordsAsync_WhenReplacingExistingKeywords_RemovesOldRelationships()
    {
        var projectContext = $"Integration.KeywordReplace.{Guid.NewGuid():N}";
        var source = CreateNode(
            id: $"{projectContext}.Source",
            name: "LegacyService",
            summary: "Handles stale reporting",
            projectContext: projectContext);
        var target = CreateNode(
            id: $"{projectContext}.Target",
            name: "ReportingService",
            summary: "Handles fresh reporting",
            projectContext: projectContext);

        try
        {
            await _codeRepository!.UpsertNodeAsync(source);
            await _codeRepository.UpsertNodeAsync(target);

            await _keywordRepository!.ReplaceKeywordsAsync(new ReplaceKeywordRelationshipsCommand
            {
                SourceNodeId = source.Id,
                KeywordTextChecksum = "first-pass",
                EnrichmentVersion = 1,
                Keywords =
                [
                    new ExtractedKeyword
                    {
                        Value = "legacy",
                        NormalizedValue = "legacy",
                        Count = 1,
                        Weight = 1.0d,
                        Sources = ["name"]
                    }
                ]
            });

            await _keywordRepository.ReplaceKeywordsAsync(new ReplaceKeywordRelationshipsCommand
            {
                SourceNodeId = target.Id,
                KeywordTextChecksum = "target-pass",
                EnrichmentVersion = 1,
                Keywords =
                [
                    new ExtractedKeyword
                    {
                        Value = "legacy",
                        NormalizedValue = "legacy",
                        Count = 1,
                        Weight = 0.7d,
                        Sources = ["summary"]
                    }
                ]
            });

            await _keywordRepository.ReplaceKeywordsAsync(new ReplaceKeywordRelationshipsCommand
            {
                SourceNodeId = source.Id,
                KeywordTextChecksum = "second-pass",
                EnrichmentVersion = 1,
                Keywords =
                [
                    new ExtractedKeyword
                    {
                        Value = "reporting",
                        NormalizedValue = "reporting",
                        Count = 1,
                        Weight = 1.0d,
                        Sources = ["summary"]
                    }
                ]
            });

            await _keywordRepository.RecalculateKeywordStatisticsAsync(projectContext);

            var related = await _keywordRepository.FindRelatedByKeywordsAsync(new KeywordRelatedNodeQuery
            {
                SourceNodeId = source.Id,
                TargetKinds = ["Class"],
                MinimumSharedKeywords = 1,
                MinimumScore = 0.1d,
                MaximumDocumentFrequencyRatio = 1.0d,
                Limit = 10
            });

            related.Should().BeEmpty("the old 'legacy' relationship should have been removed before the replacement keyword set was written");
        }
        finally
        {
            await _codeRepository!.DeleteProjectAsync(projectContext);
            await _keywordRepository!.RecalculateKeywordStatisticsAsync(projectContext);
        }
    }

    [Fact]
    public async Task ClassifyKeywordsAsync_WhenNoiseKeywordExists_ExcludesItFromRelatedLookup()
    {
        var projectContext = $"Integration.KeywordClassification.{Guid.NewGuid():N}";
        var first = CreateNode(
            id: $"{projectContext}.First",
            name: "OnlyWorkflow",
            summary: "Only processing path",
            projectContext: projectContext);
        var second = CreateNode(
            id: $"{projectContext}.Second",
            name: "OnlyKnowledge",
            summary: "Only retry note",
            projectContext: projectContext);

        try
        {
            await _codeRepository!.UpsertNodeAsync(first);
            await _codeRepository.UpsertNodeAsync(second);

            await _keywordRepository!.ReplaceKeywordsAsync(new ReplaceKeywordRelationshipsCommand
            {
                SourceNodeId = first.Id,
                KeywordTextChecksum = "classification-1",
                EnrichmentVersion = 1,
                Keywords =
                [
                    new ExtractedKeyword
                    {
                        Value = "only",
                        NormalizedValue = "only",
                        Count = 1,
                        Weight = 1.0d,
                        Sources = ["summary"]
                    }
                ]
            });

            await _keywordRepository.ReplaceKeywordsAsync(new ReplaceKeywordRelationshipsCommand
            {
                SourceNodeId = second.Id,
                KeywordTextChecksum = "classification-2",
                EnrichmentVersion = 1,
                Keywords =
                [
                    new ExtractedKeyword
                    {
                        Value = "only",
                        NormalizedValue = "only",
                        Count = 1,
                        Weight = 1.0d,
                        Sources = ["summary"]
                    }
                ]
            });

            await _keywordRepository.RecalculateKeywordStatisticsAsync(projectContext);

            var beforeClassification = await _keywordRepository.FindRelatedByKeywordsAsync(new KeywordRelatedNodeQuery
            {
                SourceNodeId = first.Id,
                TargetKinds = ["Class"],
                MinimumSharedKeywords = 1,
                MinimumScore = 0.1d,
                MaximumDocumentFrequencyRatio = 1.0d,
                Limit = 10
            });

            beforeClassification.Should().ContainSingle(match => match.TargetNodeId == second.Id);

            var keywords = await _keywordRepository.GetKeywordsForClassificationAsync(projectContext, 1);
            keywords.Should().ContainSingle();

            await _keywordRepository.SaveKeywordClassificationsAsync(
                [
                    new KeywordClassificationResult
                    {
                        KeywordId = keywords[0].Id,
                        Classification = KeywordClassification.Noise,
                        IsCommon = false,
                        IsNoise = true,
                        UsefulnessScore = 0d
                    }
                ],
                1);

            var afterClassification = await _keywordRepository.FindRelatedByKeywordsAsync(new KeywordRelatedNodeQuery
            {
                SourceNodeId = first.Id,
                TargetKinds = ["Class"],
                MinimumSharedKeywords = 1,
                MinimumScore = 0.1d,
                MaximumDocumentFrequencyRatio = 1.0d,
                Limit = 10
            });

            afterClassification.Should().BeEmpty("noise keywords should be ignored once classification metadata has been saved");
        }
        finally
        {
            await _codeRepository!.DeleteProjectAsync(projectContext);
            await _keywordRepository!.RecalculateKeywordStatisticsAsync(projectContext);
        }
    }

    private static CodeNode CreateNode(
        string id,
        string name,
        string summary,
        string projectContext) =>
        new()
        {
            Id = id,
            Name = name,
            Summary = summary,
            Type = CodeNodeType.Class,
            ProjectContext = projectContext,
            FilePath = $"src/{projectContext}/{name}.cs",
            Namespace = $"{projectContext}.KeywordGraph"
        };
}
