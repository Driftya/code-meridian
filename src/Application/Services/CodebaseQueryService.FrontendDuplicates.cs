using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CodeMeridian.Core.CodeGraph;

namespace CodeMeridian.Application.Services;

public sealed partial class CodebaseQueryService
{
    private const int FrontendStyleDeclarationQueryLimit = 10000;
    private const double RelativeNumericTolerance = 0.08;
    private const double AbsoluteNumberTolerance = 0.08;
    private const double AbsoluteLengthTolerancePx = 2.0;
    private const double AbsolutePercentageTolerance = 2.0;
    private const double AbsoluteAngleToleranceDeg = 3.0;
    private const double AbsoluteTimeToleranceMs = 50.0;
    private const double AbsoluteColorTolerance = 18.0;
    private static readonly Regex NumberWithUnitPattern = new(
        @"^(?<value>[+-]?(?:\d+\.?\d*|\.\d+))(?<unit>[a-z%]+)?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex HexColorPattern = new(
        @"^#(?<hex>[0-9a-f]{3,8})$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex RgbColorPattern = new(
        @"^rgba?\((?<content>.+)\)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex HslColorPattern = new(
        @"^hsla?\((?<content>.+)\)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Dictionary<string, string> NamedColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["black"] = "#000000",
        ["white"] = "#ffffff",
        ["red"] = "#ff0000",
        ["green"] = "#008000",
        ["blue"] = "#0000ff",
        ["transparent"] = "#00000000",
        ["currentcolor"] = "currentcolor"
    };

    private async Task<string> FindFrontendStyleDuplicateCandidatesAsync(
        string? projectContext,
        string? filter,
        bool excludeTests,
        CancellationToken cancellationToken)
    {
        var nodes = await codeGraph.QueryNodesAsync(
            new CodeGraphQuery
            {
                ProjectContext = projectContext,
                TypeFilter = CodeNodeType.ExternalConcept,
                Limit = FrontendStyleDeclarationQueryLimit
            },
            cancellationToken);

        var declarations = nodes
            .Where(node => AllowsProfile(node, AnalysisProfile.DuplicateDetection))
            .Where(node => !excludeTests || ResolveFileRole(node) != IndexedFileRole.Test)
            .Where(IsFrontendStyleDeclaration)
            .Select(TryMapStyleDeclaration)
            .OfType<StyleDeclarationFact>()
            .Where(fact => MatchesFrontendFilter(fact, filter))
            .ToArray();

        if (declarations.Length == 0)
        {
            return "No frontend style declarations were available for near-duplicate analysis. " +
                   "Re-index HTML/CSS/SCSS files, or broaden the current filters.";
        }

        var clusters = declarations
            .GroupBy(fact => fact.PatternKey, StringComparer.Ordinal)
            .SelectMany(BuildStyleClusters)
            .Where(cluster => cluster.Members.Count >= 2)
            .OrderByDescending(cluster => cluster.Members.Count)
            .ThenBy(cluster => cluster.PropertyName, StringComparer.Ordinal)
            .ThenBy(cluster => cluster.ComparisonBasis, StringComparer.Ordinal)
            .Take(20)
            .ToArray();

        if (clusters.Length == 0)
        {
            return "No near-duplicate frontend style clusters were found. " +
                   "Try re-indexing the frontend files, or broaden the style filter.";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"## Frontend Style Near-Duplicate Clusters{(projectContext is not null ? $" - {projectContext}" : "")}");
        sb.AppendLine($"**{clusters.Length}** actionable clusters from **{declarations.Length}** indexed style declarations.");
        sb.AppendLine();
        sb.AppendLine("| Property | Cluster reason | Comparison basis | Selectors/files | Raw values | Normalized values | Opportunity | Confidence/tolerance |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|");

        foreach (var cluster in clusters)
        {
            var selectors = string.Join("<br>", cluster.Members
                .Select(member => $"`{member.SelectorText}`<br>`{member.FilePath}:{member.LineNumber}`")
                .Distinct(StringComparer.Ordinal)
                .Take(4));
            var rawValues = string.Join("<br>", cluster.Members
                .Select(member => $"`{member.RawValue}`")
                .Distinct(StringComparer.Ordinal)
                .Take(4));
            var normalizedValues = string.Join("<br>", cluster.Members
                .Select(member => $"`{member.Normalized.DisplayValue}`")
                .Distinct(StringComparer.Ordinal)
                .Take(4));

            sb.AppendLine(
                $"| `{cluster.PropertyName}` | {EscapeTableCell(cluster.Reason)} | {EscapeTableCell(cluster.ComparisonBasis)} | {selectors} | {rawValues} | {normalizedValues} | {EscapeTableCell(cluster.Opportunity)} | {EscapeTableCell(cluster.ConfidenceNote)} |");
        }

        sb.AppendLine();
        sb.AppendLine("> Frontend clusters are deterministic groups of indexed CSS declaration nodes. Values are compared within the same property and normalized token shape; numeric tokens use bounded tolerances, colors use bounded channel-distance tolerance, and symbolic tokens must match exactly.");
        return sb.ToString();
    }

    private static bool IsFrontendStyleDeclaration(CodeNode node) =>
        node.Type == CodeNodeType.ExternalConcept
        && node.Properties.TryGetValue("externalKind", out var kind)
        && string.Equals(kind, "CssDeclaration", StringComparison.Ordinal);

    private static bool MatchesFrontendFilter(StyleDeclarationFact fact, string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return true;

        return fact.PropertyName.Contains(filter, StringComparison.OrdinalIgnoreCase)
               || fact.SelectorText.Contains(filter, StringComparison.OrdinalIgnoreCase)
               || fact.FilePath.Contains(filter, StringComparison.OrdinalIgnoreCase)
               || fact.RawValue.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    private static StyleDeclarationFact? TryMapStyleDeclaration(CodeNode node)
    {
        if (!node.Properties.TryGetValue("propertyName", out var propertyName)
            || !node.Properties.TryGetValue("rawValue", out var rawValue)
            || !node.Properties.TryGetValue("selectorText", out var selectorText)
            || string.IsNullOrWhiteSpace(node.FilePath)
            || node.LineNumber is null)
        {
            return null;
        }

        var normalized = NormalizeStyleValue(rawValue);
        var normalizedProperty = propertyName.Trim().ToLowerInvariant();
        var patternKey = BuildPatternKey(normalizedProperty, normalized);
        return new StyleDeclarationFact(
            node,
            normalizedProperty,
            rawValue.Trim(),
            selectorText.Trim(),
            node.FilePath!,
            node.LineNumber.Value,
            normalized,
            patternKey);
    }

    private static string BuildPatternKey(string propertyName, NormalizedStyleValue normalized)
    {
        var pattern = string.Join("|", normalized.Tokens.Select(DescribePatternToken));
        return $"{propertyName}|{pattern}";
    }

    private static string DescribePatternToken(NormalizedStyleToken token) =>
        token.Kind switch
        {
            StyleTokenKind.Numeric => $"num:{token.NumericCategory}",
            StyleTokenKind.Color => "color",
            _ => $"sym:{token.SymbolicValue}"
        };

    private static IEnumerable<StyleCluster> BuildStyleClusters(IGrouping<string, StyleDeclarationFact> group)
    {
        var ordered = group
            .OrderBy(fact => fact.Normalized.SortKey, StringComparer.Ordinal)
            .ThenBy(fact => fact.FilePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(fact => fact.LineNumber)
            .ToArray();
        var clusters = new List<StyleCluster>();

        foreach (var fact in ordered)
        {
            var cluster = clusters.FirstOrDefault(existing => CanJoinCluster(existing, fact));
            if (cluster is null)
            {
                clusters.Add(new StyleCluster(
                    fact.PropertyName,
                    BuildComparisonBasis(fact.Normalized),
                    new List<StyleDeclarationFact> { fact },
                    BuildReason(fact.Normalized),
                    InferOpportunity([fact]),
                    BuildConfidenceNote(fact.Normalized),
                    fact.Normalized.Tokens));
                continue;
            }

            cluster.Members.Add(fact);
            cluster.Opportunity = InferOpportunity(cluster.Members);
        }

        return clusters;
    }

    private static bool CanJoinCluster(StyleCluster cluster, StyleDeclarationFact fact)
    {
        if (!string.Equals(cluster.PropertyName, fact.PropertyName, StringComparison.Ordinal))
            return false;

        if (cluster.RepresentativeTokens.Count != fact.Normalized.Tokens.Count)
            return false;

        for (var i = 0; i < cluster.RepresentativeTokens.Count; i++)
        {
            if (!AreTokensComparable(cluster.RepresentativeTokens[i], fact.Normalized.Tokens[i]))
                return false;
        }

        return true;
    }

    private static bool AreTokensComparable(NormalizedStyleToken representative, NormalizedStyleToken candidate)
    {
        if (representative.Kind != candidate.Kind)
            return false;

        return representative.Kind switch
        {
            StyleTokenKind.Numeric => string.Equals(representative.NumericCategory, candidate.NumericCategory, StringComparison.Ordinal)
                                      && AreNumbersNear(representative.NumericValue ?? 0, candidate.NumericValue ?? 0, representative.NumericCategory),
            StyleTokenKind.Color => AreColorsNear(representative, candidate),
            _ => string.Equals(representative.SymbolicValue, candidate.SymbolicValue, StringComparison.Ordinal)
        };
    }

    private static bool AreNumbersNear(double left, double right, string? category)
    {
        var absoluteTolerance = category switch
        {
            "length-px" => AbsoluteLengthTolerancePx,
            "percentage" => AbsolutePercentageTolerance,
            "angle-deg" => AbsoluteAngleToleranceDeg,
            "time-ms" => AbsoluteTimeToleranceMs,
            _ => AbsoluteNumberTolerance
        };
        var delta = Math.Abs(left - right);
        if (delta <= absoluteTolerance)
            return true;

        var scale = Math.Max(Math.Max(Math.Abs(left), Math.Abs(right)), 1d);
        return (delta / scale) <= RelativeNumericTolerance;
    }

    private static bool AreColorsNear(NormalizedStyleToken left, NormalizedStyleToken right)
    {
        if (left.Color is null || right.Color is null)
            return string.Equals(left.SymbolicValue, right.SymbolicValue, StringComparison.Ordinal);

        var leftColor = left.Color.Value;
        var rightColor = right.Color.Value;
        var dr = leftColor.Red - rightColor.Red;
        var dg = leftColor.Green - rightColor.Green;
        var db = leftColor.Blue - rightColor.Blue;
        var da = leftColor.Alpha - rightColor.Alpha;
        var distance = Math.Sqrt((dr * dr) + (dg * dg) + (db * db) + (da * da));
        return distance <= AbsoluteColorTolerance;
    }

    private static string BuildComparisonBasis(NormalizedStyleValue normalized) =>
        string.Join(" ", normalized.Tokens.Select(token => token.DisplayValue));

    private static string BuildReason(NormalizedStyleValue normalized)
    {
        if (normalized.Tokens.Any(token => token.Kind == StyleTokenKind.Color))
            return "same property and token shape with bounded color drift";

        if (normalized.Tokens.Any(token => token.Kind == StyleTokenKind.Numeric))
            return "same property and token shape with bounded numeric/unit drift";

        return "same property and exact symbolic value shape";
    }

    private static string BuildConfidenceNote(NormalizedStyleValue normalized)
    {
        if (normalized.Tokens.Any(token => token.Kind == StyleTokenKind.Color))
            return $"colors within Euclidean RGBA distance <= {AbsoluteColorTolerance.ToString("F0", CultureInfo.InvariantCulture)}";

        if (normalized.Tokens.Any(token => token.Kind == StyleTokenKind.Numeric))
            return "numeric tokens normalized by unit family with bounded absolute and relative tolerance";

        return "symbolic tokens must match exactly after whitespace normalization";
    }

    private static string InferOpportunity(IReadOnlyCollection<StyleDeclarationFact> members)
    {
        var selectorStems = members
            .Select(member => ExtractSelectorStem(member.SelectorText))
            .Where(stem => stem.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return selectorStems.Length == 1 && members.Count >= 2
            ? $"base-class variant review around `{selectorStems[0]}`"
            : "shared style token candidate";
    }

    private static string ExtractSelectorStem(string selectorText)
    {
        var value = selectorText.Trim();
        var modifierIndex = value.IndexOf("--", StringComparison.Ordinal);
        if (modifierIndex > 0)
            return value[..modifierIndex];

        modifierIndex = value.IndexOf("__", StringComparison.Ordinal);
        if (modifierIndex > 0)
            return value[..modifierIndex];

        var separatorIndex = value.IndexOfAny([' ', ':', '[', '>']);
        return separatorIndex > 0 ? value[..separatorIndex] : value;
    }

    private static NormalizedStyleValue NormalizeStyleValue(string rawValue)
    {
        var rawTokens = TokenizeStyleValue(rawValue);
        var normalizedTokens = rawTokens.Select(NormalizeStyleToken).ToArray();
        var displayValue = string.Join(" ", normalizedTokens.Select(token => token.DisplayValue));
        var sortKey = string.Join("|", normalizedTokens.Select(token => token.SortKey));
        return new NormalizedStyleValue(displayValue, sortKey, normalizedTokens);
    }

    private static IReadOnlyList<string> TokenizeStyleValue(string rawValue)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        for (var i = 0; i < rawValue.Length; i++)
        {
            var ch = rawValue[i];
            if (char.IsWhiteSpace(ch))
            {
                FlushCurrent();
                continue;
            }

            if (ch is ',' or '/')
            {
                FlushCurrent();
                tokens.Add(ch.ToString());
                continue;
            }

            if (ch is '"' or '\'')
            {
                FlushCurrent();
                var end = i + 1;
                while (end < rawValue.Length && rawValue[end] != ch)
                    end++;
                tokens.Add(rawValue[i..Math.Min(end + 1, rawValue.Length)]);
                i = Math.Min(end, rawValue.Length - 1);
                continue;
            }

            if (IsFunctionStart(rawValue, i))
            {
                FlushCurrent();
                var end = FindBalancedFunctionEnd(rawValue, i);
                tokens.Add(rawValue[i..end]);
                i = end - 1;
                continue;
            }

            current.Append(ch);
        }

        FlushCurrent();
        return tokens;

        void FlushCurrent()
        {
            if (current.Length == 0)
                return;

            tokens.Add(current.ToString());
            current.Clear();
        }
    }

    private static bool IsFunctionStart(string value, int index)
    {
        if (index >= value.Length || !char.IsLetter(value[index]))
            return false;

        var openIndex = value.IndexOf('(', index);
        if (openIndex <= index)
            return false;

        for (var i = index; i < openIndex; i++)
        {
            if (!(char.IsLetterOrDigit(value[i]) || value[i] is '-' or '_'))
                return false;
        }

        return true;
    }

    private static int FindBalancedFunctionEnd(string value, int startIndex)
    {
        var depth = 0;
        for (var i = startIndex; i < value.Length; i++)
        {
            if (value[i] == '(')
                depth++;
            else if (value[i] == ')')
            {
                depth--;
                if (depth == 0)
                    return i + 1;
            }
        }

        return value.Length;
    }

    private static NormalizedStyleToken NormalizeStyleToken(string rawToken)
    {
        var token = rawToken.Trim();
        if (token.Length == 0)
            return new NormalizedStyleToken(StyleTokenKind.Symbolic, string.Empty, string.Empty, "empty", null, null, null);

        if (TryNormalizeColor(token, out var colorToken))
            return colorToken;

        var match = NumberWithUnitPattern.Match(token);
        if (match.Success && double.TryParse(match.Groups["value"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            var unit = match.Groups["unit"].Success ? match.Groups["unit"].Value.ToLowerInvariant() : string.Empty;
            return NormalizeNumericToken(token, value, unit);
        }

        return new NormalizedStyleToken(
            StyleTokenKind.Symbolic,
            NormalizeSymbolicToken(token),
            NormalizeSymbolicToken(token),
            $"sym:{NormalizeSymbolicToken(token)}",
            null,
            null,
            null);
    }

    private static NormalizedStyleToken NormalizeNumericToken(string rawToken, double value, string unit)
    {
        var (normalizedValue, normalizedUnit, category) = unit switch
        {
            "px" => (value, "px", "length-px"),
            "rem" => (value * 16d, "px", "length-px"),
            "em" => (value * 16d, "px", "length-px"),
            "%" => (value, "%", "percentage"),
            "deg" => (value, "deg", "angle-deg"),
            "rad" => (value * (180d / Math.PI), "deg", "angle-deg"),
            "turn" => (value * 360d, "deg", "angle-deg"),
            "ms" => (value, "ms", "time-ms"),
            "s" => (value * 1000d, "ms", "time-ms"),
            "" => (value, string.Empty, "number"),
            _ => (value, unit, $"unit:{unit}")
        };

        var display = normalizedUnit.Length == 0
            ? normalizedValue.ToString("0.###", CultureInfo.InvariantCulture)
            : $"{normalizedValue.ToString("0.###", CultureInfo.InvariantCulture)}{normalizedUnit}";
        return new NormalizedStyleToken(
            StyleTokenKind.Numeric,
            display,
            $"{category}:{normalizedValue.ToString("0.###", CultureInfo.InvariantCulture)}",
            $"{category}:{normalizedValue.ToString("0.###", CultureInfo.InvariantCulture)}",
            normalizedValue,
            category,
            null);
    }

    private static string NormalizeSymbolicToken(string token)
    {
        var compact = Regex.Replace(token.Trim().ToLowerInvariant(), @"\s+", " ");
        compact = compact.Replace("( ", "(", StringComparison.Ordinal).Replace(" )", ")", StringComparison.Ordinal);
        compact = compact.Replace(" ,", ",", StringComparison.Ordinal);
        return compact;
    }

    private static bool TryNormalizeColor(string token, out NormalizedStyleToken normalized)
    {
        if (NamedColors.TryGetValue(token, out var named))
        {
            if (string.Equals(named, "currentcolor", StringComparison.Ordinal))
            {
                normalized = new NormalizedStyleToken(StyleTokenKind.Symbolic, "currentcolor", "currentcolor", "sym:currentcolor", null, null, null);
                return true;
            }

            token = named;
        }

        if (TryParseHexColor(token, out var color)
            || TryParseRgbColor(token, out color)
            || TryParseHslColor(token, out color))
        {
            var display = color.ToHexDisplay();
            normalized = new NormalizedStyleToken(
                StyleTokenKind.Color,
                display,
                display,
                $"color:{display}",
                null,
                null,
                color);
            return true;
        }

        normalized = new NormalizedStyleToken(StyleTokenKind.Symbolic, token, token, $"sym:{token}", null, null, null);
        return false;
    }

    private static bool TryParseHexColor(string token, out RgbaColor color)
    {
        var match = HexColorPattern.Match(token);
        if (!match.Success)
        {
            color = default;
            return false;
        }

        var hex = match.Groups["hex"].Value;
        if (hex.Length is 3 or 4)
        {
            hex = string.Concat(hex.Select(ch => $"{ch}{ch}"));
        }

        if (hex.Length == 6)
            hex += "ff";

        if (hex.Length != 8)
        {
            color = default;
            return false;
        }

        color = new RgbaColor(
            Convert.ToInt32(hex[0..2], 16),
            Convert.ToInt32(hex[2..4], 16),
            Convert.ToInt32(hex[4..6], 16),
            Convert.ToInt32(hex[6..8], 16));
        return true;
    }

    private static bool TryParseRgbColor(string token, out RgbaColor color)
    {
        var match = RgbColorPattern.Match(token);
        if (!match.Success)
        {
            color = default;
            return false;
        }

        var parts = match.Groups["content"].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is not (3 or 4))
        {
            color = default;
            return false;
        }

        if (!TryParseRgbChannel(parts[0], out var red)
            || !TryParseRgbChannel(parts[1], out var green)
            || !TryParseRgbChannel(parts[2], out var blue))
        {
            color = default;
            return false;
        }

        var alpha = 255;
        if (parts.Length == 4 && !TryParseAlpha(parts[3], out alpha))
        {
            color = default;
            return false;
        }

        color = new RgbaColor(red, green, blue, alpha);
        return true;
    }

    private static bool TryParseRgbChannel(string value, out int channel)
    {
        value = value.Trim();
        if (value.EndsWith("%", StringComparison.Ordinal)
            && double.TryParse(value[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var percent))
        {
            channel = (int)Math.Round(Math.Clamp(percent, 0d, 100d) * 2.55d);
            return true;
        }

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            channel = (int)Math.Round(Math.Clamp(number, 0d, 255d));
            return true;
        }

        channel = 0;
        return false;
    }

    private static bool TryParseAlpha(string value, out int alpha)
    {
        value = value.Trim();
        if (value.EndsWith("%", StringComparison.Ordinal)
            && double.TryParse(value[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var percent))
        {
            alpha = (int)Math.Round(Math.Clamp(percent, 0d, 100d) * 2.55d);
            return true;
        }

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            alpha = number <= 1d
                ? (int)Math.Round(Math.Clamp(number, 0d, 1d) * 255d)
                : (int)Math.Round(Math.Clamp(number, 0d, 255d));
            return true;
        }

        alpha = 0;
        return false;
    }

    private static bool TryParseHslColor(string token, out RgbaColor color)
    {
        var match = HslColorPattern.Match(token);
        if (!match.Success)
        {
            color = default;
            return false;
        }

        var parts = match.Groups["content"].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is not (3 or 4))
        {
            color = default;
            return false;
        }

        if (!double.TryParse(parts[0].Replace("deg", string.Empty, StringComparison.OrdinalIgnoreCase), NumberStyles.Float, CultureInfo.InvariantCulture, out var hue)
            || !TryParsePercentage(parts[1], out var saturation)
            || !TryParsePercentage(parts[2], out var lightness))
        {
            color = default;
            return false;
        }

        var alpha = 255;
        if (parts.Length == 4 && !TryParseAlpha(parts[3], out alpha))
        {
            color = default;
            return false;
        }

        color = FromHsl(hue, saturation / 100d, lightness / 100d, alpha);
        return true;
    }

    private static bool TryParsePercentage(string value, out double percent)
    {
        value = value.Trim();
        if (!value.EndsWith("%", StringComparison.Ordinal)
            || !double.TryParse(value[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out percent))
        {
            percent = 0;
            return false;
        }

        percent = Math.Clamp(percent, 0d, 100d);
        return true;
    }

    private static RgbaColor FromHsl(double hue, double saturation, double lightness, int alpha)
    {
        hue = ((hue % 360d) + 360d) % 360d;
        var chroma = (1d - Math.Abs((2d * lightness) - 1d)) * saturation;
        var x = chroma * (1d - Math.Abs(((hue / 60d) % 2d) - 1d));
        var m = lightness - (chroma / 2d);

        var (r1, g1, b1) = hue switch
        {
            < 60d => (chroma, x, 0d),
            < 120d => (x, chroma, 0d),
            < 180d => (0d, chroma, x),
            < 240d => (0d, x, chroma),
            < 300d => (x, 0d, chroma),
            _ => (chroma, 0d, x)
        };

        return new RgbaColor(
            (int)Math.Round((r1 + m) * 255d),
            (int)Math.Round((g1 + m) * 255d),
            (int)Math.Round((b1 + m) * 255d),
            alpha);
    }

    private sealed record StyleDeclarationFact(
        CodeNode Node,
        string PropertyName,
        string RawValue,
        string SelectorText,
        string FilePath,
        int LineNumber,
        NormalizedStyleValue Normalized,
        string PatternKey);

    private sealed record NormalizedStyleValue(string DisplayValue, string SortKey, IReadOnlyList<NormalizedStyleToken> Tokens);

    private sealed class StyleCluster(
        string propertyName,
        string comparisonBasis,
        List<StyleDeclarationFact> members,
        string reason,
        string opportunity,
        string confidenceNote,
        IReadOnlyList<NormalizedStyleToken> representativeTokens)
    {
        public string PropertyName { get; } = propertyName;
        public string ComparisonBasis { get; } = comparisonBasis;
        public List<StyleDeclarationFact> Members { get; } = members;
        public string Reason { get; } = reason;
        public string Opportunity { get; set; } = opportunity;
        public string ConfidenceNote { get; } = confidenceNote;
        public IReadOnlyList<NormalizedStyleToken> RepresentativeTokens { get; } = representativeTokens;
    }

    private sealed record NormalizedStyleToken(
        StyleTokenKind Kind,
        string DisplayValue,
        string SymbolicValue,
        string SortKey,
        double? NumericValue,
        string? NumericCategory,
        RgbaColor? Color);

    private enum StyleTokenKind
    {
        Numeric,
        Color,
        Symbolic
    }

    private readonly record struct RgbaColor(int Red, int Green, int Blue, int Alpha)
    {
        public string ToHexDisplay() =>
            Alpha == 255
                ? $"#{Red:x2}{Green:x2}{Blue:x2}"
                : $"#{Red:x2}{Green:x2}{Blue:x2}{Alpha:x2}";
    }
}
