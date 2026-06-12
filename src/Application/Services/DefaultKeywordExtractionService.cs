using System.Collections.Frozen;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using CodeMeridian.Core.KeywordGraph;
using Microsoft.Extensions.Options;

namespace CodeMeridian.Application.Services;

public sealed partial class DefaultKeywordExtractionService(
    IOptions<KeywordEnrichmentOptions> options) : IKeywordExtractionService
{
    private static readonly FrozenSet<string> BaseStopwords = BuildBaseStopwords();
    private static readonly FrozenDictionary<string, FrozenDictionary<string, double>> SourceWeights = BuildSourceWeights();
    private readonly KeywordEnrichmentOptions keywordOptions = options.Value;

    public KeywordExtractionResult Extract(KeywordSourceNode input)
    {
        var projected = ProjectText(input);
        var checksum = ComputeChecksum(projected);
        var allowedShortTerms = keywordOptions.AllowedShortTerms
            .Select(term => term.Trim().ToLowerInvariant())
            .Where(term => term.Length > 0)
            .ToFrozenSet(StringComparer.Ordinal);
        var stopwords = BaseStopwords
            .Union(keywordOptions.AdditionalStopwords
                .Select(word => word.Trim().ToLowerInvariant())
                .Where(word => word.Length > 0))
            .ToFrozenSet(StringComparer.Ordinal);

        var aggregates = new Dictionary<string, KeywordAggregate>(StringComparer.Ordinal);

        foreach (var (source, text) in projected)
        {
            if (string.IsNullOrWhiteSpace(text))
                continue;

            foreach (var token in Tokenize(text))
            {
                if (!ShouldKeep(token, allowedShortTerms, stopwords))
                    continue;

                if (!aggregates.TryGetValue(token, out var aggregate))
                {
                    aggregate = new KeywordAggregate(token);
                    aggregates[token] = aggregate;
                }

                aggregate.Count++;
                aggregate.Sources.Add(source);
            }
        }

        var kindWeights = SourceWeights.TryGetValue(input.Kind, out var specificWeights)
            ? specificWeights
            : SourceWeights["CodeNode"];

        var keywords = aggregates.Values
            .Select(item =>
            {
                var orderedSources = item.Sources
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(static source => source, StringComparer.Ordinal)
                    .ToArray();
                var weight = orderedSources.Sum(source => GetSourceWeight(kindWeights, source)) * LocalFrequencyWeight(item.Count);

                return new ExtractedKeyword
                {
                    Value = item.Value,
                    NormalizedValue = item.Value,
                    Count = item.Count,
                    Weight = Math.Round(weight, 4, MidpointRounding.AwayFromZero),
                    Sources = orderedSources
                };
            })
            .OrderByDescending(static keyword => keyword.Weight)
            .ThenByDescending(static keyword => keyword.Count)
            .ThenBy(static keyword => keyword.NormalizedValue, StringComparer.Ordinal)
            .Take(keywordOptions.MaximumKeywordsPerNode)
            .ToArray();

        return new KeywordExtractionResult(checksum, keywords);
    }

    private IReadOnlyDictionary<string, string> ProjectText(KeywordSourceNode input)
    {
        var projected = new SortedDictionary<string, string>(StringComparer.Ordinal);

        foreach (var (source, text) in input.TextBySource)
        {
            if (!string.IsNullOrWhiteSpace(text))
                projected[source] = text;
        }

        if (projected.Count == 0 && !string.IsNullOrWhiteSpace(input.Id))
            projected["id"] = input.Id;

        return projected;
    }

    private static double LocalFrequencyWeight(int count) =>
        1d + Math.Log10(Math.Max(1, count));

    private static double GetSourceWeight(
        FrozenDictionary<string, double> weights,
        string source) =>
        weights.TryGetValue(source, out var weight) ? weight : 0.2d;

    private bool ShouldKeep(
        string token,
        FrozenSet<string> allowedShortTerms,
        FrozenSet<string> stopwords)
    {
        if (token.Length == 0)
            return false;

        if (GuidLikePattern().IsMatch(token))
            return false;

        if (HexBlobPattern().IsMatch(token))
            return false;

        if (token.All(static character => char.IsDigit(character)))
            return false;

        if (stopwords.Contains(token))
            return false;

        return token.Length >= keywordOptions.MinimumKeywordLength || allowedShortTerms.Contains(token);
    }

    private static IEnumerable<string> Tokenize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            yield break;

        var expanded = ExpandBoundaries(value);

        foreach (var part in SplitPattern().Split(expanded))
        {
            if (string.IsNullOrWhiteSpace(part))
                continue;

            var normalized = part.Trim().ToLowerInvariant();
            if (normalized.Length == 0)
                continue;

            yield return normalized;
        }
    }

    private static string ExpandBoundaries(string value)
    {
        var builder = new StringBuilder(value.Length * 2);

        for (var index = 0; index < value.Length; index++)
        {
            var current = value[index];

            if (IsSeparator(current))
            {
                builder.Append(' ');
                continue;
            }

            if (index > 0)
            {
                var previous = value[index - 1];
                var next = index + 1 < value.Length ? value[index + 1] : '\0';

                if (NeedsBoundary(previous, current, next))
                    builder.Append(' ');
            }

            builder.Append(current);
        }

        return builder.ToString();
    }

    private static bool NeedsBoundary(char previous, char current, char next) =>
        (char.IsLower(previous) && char.IsUpper(current))
        || (char.IsLetter(previous) && char.IsDigit(current))
        || (char.IsDigit(previous) && char.IsLetter(current))
        || (char.IsUpper(previous) && char.IsUpper(current) && char.IsLower(next));

    private static bool IsSeparator(char value) =>
        value is '_' or '-' or '.' or '/' or '\\' or ':' || char.IsWhiteSpace(value) || char.IsPunctuation(value) && value is not '\'';

    private static string ComputeChecksum(IReadOnlyDictionary<string, string> projected)
    {
        var builder = new StringBuilder();

        foreach (var (source, text) in projected)
        {
            builder.Append(source);
            builder.Append(':');
            builder.Append(text);
            builder.Append('\n');
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static FrozenSet<string> BuildBaseStopwords() =>
        new[]
        {
            "the",
            "and",
            "for",
            "with",
            "from",
            "into",
            "this",
            "that",
            "new",
            "get",
            "set",
            "has",
            "had",
            "can",
            "use",
            "used",
            "using",
            "class",
            "record",
            "string",
            "bool",
            "void",
            "task",
            "async",
            "await",
            "public",
            "private",
            "internal",
            "static",
            "sealed",
            "return",
            "null",
            "true",
            "false"
        }.ToFrozenSet(StringComparer.Ordinal);

    private static FrozenDictionary<string, FrozenDictionary<string, double>> BuildSourceWeights() =>
        new Dictionary<string, FrozenDictionary<string, double>>(StringComparer.Ordinal)
        {
            ["CodeNode"] = new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["name"] = 1.0d,
                ["summary"] = 0.85d,
                ["namespace"] = 0.35d,
                ["filePath"] = 0.35d,
                ["type"] = 0.2d
            }.ToFrozenDictionary(StringComparer.Ordinal),
            ["ApiEndpoint"] = new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["name"] = 1.0d,
                ["summary"] = 0.9d,
                ["namespace"] = 0.4d,
                ["filePath"] = 0.35d,
                ["type"] = 0.2d
            }.ToFrozenDictionary(StringComparer.Ordinal),
            ["Diagnostic"] = new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["name"] = 0.65d,
                ["summary"] = 0.9d,
                ["namespace"] = 0.4d,
                ["filePath"] = 0.35d,
                ["type"] = 0.2d
            }.ToFrozenDictionary(StringComparer.Ordinal),
            ["ExternalConcept"] = new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["name"] = 1.0d,
                ["summary"] = 0.6d,
                ["namespace"] = 0.2d,
                ["filePath"] = 0.2d,
                ["type"] = 0.2d
            }.ToFrozenDictionary(StringComparer.Ordinal),
            ["KnowledgeDocument"] = new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["title"] = 1.0d,
                ["content"] = 0.6d,
                ["source"] = 0.35d,
                ["kind"] = 0.2d
            }.ToFrozenDictionary(StringComparer.Ordinal)
        }.ToFrozenDictionary(StringComparer.Ordinal);

    [GeneratedRegex("[^\\p{L}\\p{Nd}]+", RegexOptions.Compiled)]
    private static partial Regex SplitPattern();

    [GeneratedRegex("^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex GuidLikePattern();

    [GeneratedRegex("^[0-9a-f]{16,}$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex HexBlobPattern();

    private sealed class KeywordAggregate(string value)
    {
        public string Value { get; } = value;
        public int Count { get; set; }
        public HashSet<string> Sources { get; } = new(StringComparer.Ordinal);
    }
}
