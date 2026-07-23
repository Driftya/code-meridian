using System.Text;
using CodeMeridian.Core.CodeGraph;

namespace CodeMeridian.Application.Services;

public sealed partial class CodebaseQueryService
{
    private static readonly string[] GenericMethodTerms =
    [
        "handle",
        "process",
        "execute",
        "validate",
        "update",
        "create",
        "get",
        "set"
    ];

    private static string SelectResponsibilitySliceName(ResponsibilityMethodSignals signal)
    {
        var text = string.Join(' ', new[]
        {
            signal.Method.Name,
            signal.Method.Summary ?? string.Empty,
            string.Join(' ', signal.WorkflowCallers.Select(caller => caller.Name)),
            string.Join(' ', signal.Dependencies.Select(dependency => dependency.Name))
        });

        if (ContainsAny(text, "context", "minimal", "editing", "pack"))
            return "ContextPacks";
        if (ContainsAny(text, "impact", "downstream", "caller", "connection", "blast"))
            return "Impact";
        if (ContainsAny(text, "diagnostic", "fresh", "drift", "stale"))
            return "Diagnostics";
        if (ContainsAny(text, "knowledge", "document", "docs", "keyword"))
            return "Knowledge";
        if (ContainsAny(text, "config", "option", "setting"))
            return "Configuration";
        if (ContainsAny(text, "search", "query", "find", "resolve"))
            return "Search";
        if (signal.WorkflowCallers.Count > 0)
            return ToPascalPlural(CleanWorkflowName(signal.WorkflowCallers[0]));
        if (signal.Dependencies.Count > 0)
            return ToPascalPlural(CleanName(signal.Dependencies[0].Name));

        var prefix = ExtractNonGenericPrefix(signal.Method.Name);
        return string.IsNullOrWhiteSpace(prefix) ? "Deferred" : ToPascalPlural(prefix);
    }

    private static bool ContainsAny(string value, params string[] terms) =>
        terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static string ExtractNonGenericPrefix(string methodName)
    {
        var words = SplitIdentifier(methodName).Where(word => !GenericMethodTerms.Contains(word, StringComparer.OrdinalIgnoreCase)).ToArray();
        return words.FirstOrDefault() ?? string.Empty;
    }

    private static string ToPluralFeatureName(string value)
    {
        var cleaned = CleanName(value);
        return PluralizeIdentifier(cleaned);
    }

    private static string ToPascalPlural(string value)
    {
        var cleaned = CleanName(value);
        if (string.IsNullOrWhiteSpace(cleaned))
            return "Deferred";

        return PluralizeIdentifier(cleaned);
    }

    private static string CleanName(string value)
    {
        var identifier = ExtractRelevantIdentifier(value);
        var words = SplitIdentifier(RemoveSuffix(RemoveInterfacePrefix(identifier), "Async"))
            .Where(word => !GenericMethodTerms.Contains(word, StringComparer.OrdinalIgnoreCase))
            .Take(3)
            .ToArray();

        return words.Length == 0
            ? value
            : string.Concat(words.Select(word => char.ToUpperInvariant(word[0]) + word[1..]));
    }

    private static string ExtractRelevantIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        var withoutParameters = value.Split('(', 2, StringSplitOptions.TrimEntries)[0];
        var lastSegment = withoutParameters.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault();
        return string.IsNullOrWhiteSpace(lastSegment) ? withoutParameters : lastSegment;
    }

    private static string CleanWorkflowName(CodeNode node)
    {
        if (node.Type == CodeNodeType.ApiEndpoint)
        {
            var path = node.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .LastOrDefault(part => part.Contains('/', StringComparison.Ordinal));
            var segment = path?.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .LastOrDefault();
            if (!string.IsNullOrWhiteSpace(segment))
                return CleanName(segment);
        }

        return CleanName(node.Name);
    }

    private static IReadOnlyList<string> SplitIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return [];

        var words = new List<string>();
        var current = new StringBuilder();
        foreach (var ch in value)
        {
            if (!char.IsLetterOrDigit(ch))
            {
                AddCurrentWord(words, current);
                continue;
            }

            if (current.Length > 0 && char.IsUpper(ch) && !char.IsUpper(current[^1]))
                AddCurrentWord(words, current);

            current.Append(char.ToLowerInvariant(ch));
        }

        AddCurrentWord(words, current);
        return words;
    }

    private static void AddCurrentWord(ICollection<string> words, StringBuilder current)
    {
        if (current.Length == 0)
            return;

        words.Add(current.ToString());
        current.Clear();
    }

    private static string RemoveSuffix(string value, string suffix) =>
        value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ? value[..^suffix.Length] : value;

    private static string RemovePrefix(string value, string prefix) =>
        value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && value.Length > prefix.Length ? value[prefix.Length..] : value;

    private static string RemoveInterfacePrefix(string value) =>
        value.Length > 1 && value[0] == 'I' && char.IsUpper(value[1]) ? value[1..] : value;

    private static string PluralizeIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        if (value.EndsWith("ies", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith("ses", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith("xes", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith("zes", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith("ches", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith("shes", StringComparison.OrdinalIgnoreCase))
            return value;

        if (value.EndsWith("y", StringComparison.OrdinalIgnoreCase)
            && value.Length > 1
            && !IsVowel(value[^2]))
            return value[..^1] + "ies";

        if (value.EndsWith("s", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith("x", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith("z", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith("ch", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith("sh", StringComparison.OrdinalIgnoreCase))
            return value + "es";

        return value + "s";
    }

    private static bool IsVowel(char value) =>
        value is 'a' or 'e' or 'i' or 'o' or 'u'
            or 'A' or 'E' or 'I' or 'O' or 'U';

    private static string NormalizePath(string? path) => path?.Replace('\\', '/') ?? string.Empty;

    private static bool IsVagueResponsibilityName(string name) =>
        ContainsAny(name, "Lifecycle", "Manager", "Helper", "Processor", "Operations", "Logic");

    private sealed record ResponsibilityMethodSignals(
        CodeNode Method,
        string SliceName,
        IReadOnlyList<CodeNode> Dependencies,
        IReadOnlyList<CodeNode> ProductionCallers,
        IReadOnlyList<CodeNode> WorkflowCallers,
        IReadOnlyList<CodeNode> RelatedTests)
    {
        public static ResponsibilityMethodSignals Create(
            CodeNode method,
            EditingContext context,
            IReadOnlyList<CodeNode> relatedTests,
            Func<CodeNode, bool> isTestNode)
        {
            var dependencies = context.Callees
                .Where(node => !isTestNode(node))
                .Where(node => node.Type is CodeNodeType.Class or CodeNodeType.Interface or CodeNodeType.Method or CodeNodeType.ConfigurationKey or CodeNodeType.ApiEndpoint)
                .Where(node => !IsFrameworkSignatureDependency(node))
                .DistinctBy(node => node.Id)
                .ToArray();
            var productionCallers = context.Callers
                .Where(node => !isTestNode(node))
                .DistinctBy(node => node.Id)
                .ToArray();
            var workflowCallers = productionCallers
                .Where(IsWorkflowNode)
                .ToArray();
            var signal = new ResponsibilityMethodSignals(method, string.Empty, dependencies, productionCallers, workflowCallers, relatedTests);

            return signal with { SliceName = SelectResponsibilitySliceName(signal) };
        }

        private static bool IsFrameworkSignatureDependency(CodeNode node)
        {
            var identifier = ExtractRelevantIdentifier(node.Name);
            return identifier is "CancellationToken" or "Task" or "ValueTask" or "String" or "Int32" or "Boolean" or "Object"
                || string.Equals(node.Namespace, "System", StringComparison.Ordinal)
                || node.Namespace?.StartsWith("System.Threading", StringComparison.Ordinal) == true;
        }

        private static bool IsWorkflowNode(CodeNode node)
        {
            if (node.Type is CodeNodeType.ApiEndpoint or CodeNodeType.ExternalConcept)
                return true;

            var text = $"{node.Name} {node.Namespace} {node.FilePath}";
            return ContainsAny(text, "Tool", "Controller", "Endpoint", "Command", "Cli", "Workflow");
        }
    }

    private sealed record ResponsibilitySlice(
        string Name,
        string RecommendedTypeName,
        IReadOnlyList<ResponsibilityMethodSignals> Methods,
        IReadOnlyList<CodeNode> RelatedTests,
        int Score,
        string Confidence,
        string CommunitySignal,
        double SharedEvidenceRatio,
        string Reason)
    {
        public static ResponsibilitySlice Create(
            string name,
            IReadOnlyList<ResponsibilityMethodSignals> methods,
            IReadOnlyList<string> docSources,
            ResponsibilityCommunityAdvice communityAdvice,
            string defaultServiceSuffix,
            int minimumCommunityEvidenceMethodsForHighConfidence)
        {
            var dependencies = methods.SelectMany(method => method.Dependencies).DistinctBy(node => node.Id).ToArray();
            var callers = methods.SelectMany(method => method.WorkflowCallers).DistinctBy(node => node.Id).ToArray();
            var tests = methods.SelectMany(method => method.RelatedTests).DistinctBy(node => node.Id).ToArray();
            var baseScore = methods.Count
                            + dependencies.Length * 5
                            + callers.Length * 4
                            + tests.Length * 4
                            + Math.Min(docSources.Count, 3) * 2;

            if (methods.All(method => method.Dependencies.Count == 0 && method.WorkflowCallers.Count == 0 && method.RelatedTests.Count == 0))
                baseScore -= 3;

            var score = baseScore + communityAdvice.Bonus;

            var sharedEvidenceRatio = CalculateSharedEvidenceRatio(methods);
            var confidence = score >= 18 && communityAdvice.EvidenceMethodCount >= minimumCommunityEvidenceMethodsForHighConfidence
                ? "High"
                : score >= 9 ? "Medium" : "Low";
            var evidence = new List<string>
            {
                $"{methods.Count} methods"
            };
            if (dependencies.Length > 0)
                evidence.Add($"{dependencies.Length} shared dependencies");
            if (callers.Length > 0)
                evidence.Add($"{callers.Length} workflow callers");
            if (tests.Length > 0)
                evidence.Add($"{tests.Length} related tests");
            if (docSources.Count > 0)
                evidence.Add($"{Math.Min(docSources.Count, 3)} docs");
            if (communityAdvice.HasSignal)
                evidence.Add(communityAdvice.Summary);
            if (methods.Count > 1)
                evidence.Add($"{sharedEvidenceRatio:P0} pairwise shared-evidence ratio");

            return new ResponsibilitySlice(
                name,
                BuildServiceName(name, defaultServiceSuffix),
                methods,
                tests,
                score,
                confidence,
                communityAdvice.HasSignal ? communityAdvice.Summary : "no supporting community signal",
                sharedEvidenceRatio,
                string.Join(", ", evidence));
        }

        private static double CalculateSharedEvidenceRatio(IReadOnlyList<ResponsibilityMethodSignals> methods)
        {
            if (methods.Count < 2)
                return 1d;

            var supportedPairs = 0;
            var pairCount = 0;
            for (var left = 0; left < methods.Count - 1; left++)
            {
                var leftEvidence = EvidenceIds(methods[left]);
                for (var right = left + 1; right < methods.Count; right++)
                {
                    pairCount++;
                    if (leftEvidence.Overlaps(EvidenceIds(methods[right])))
                        supportedPairs++;
                }
            }

            return pairCount == 0 ? 0d : (double)supportedPairs / pairCount;
        }

        private static HashSet<string> EvidenceIds(ResponsibilityMethodSignals method) => method.Dependencies
            .Concat(method.WorkflowCallers)
            .Concat(method.RelatedTests)
            .Select(node => node.Id)
            .ToHashSet(StringComparer.Ordinal);

        private static string BuildServiceName(string name, string defaultServiceSuffix)
        {
            var normalized = SingularizeIdentifier(name);
            var suffix = string.IsNullOrWhiteSpace(defaultServiceSuffix) ? "Service" : defaultServiceSuffix.Trim();
            return normalized switch
            {
                "ContextPack" => AppendSuffix("ContextPackBuilder", suffix),
                "Impact" => AppendSuffix("ImpactAnalysis", suffix),
                "Diagnostic" => AppendSuffix("CodeDiagnosticsQuery", suffix),
                "Knowledge" => AppendSuffix("RelatedKnowledgeQuery", suffix),
                "Search" => AppendSuffix("CodebaseSearch", suffix),
                "Configuration" => AppendSuffix("ConfigurationQuery", suffix),
                _ => AppendSuffix(normalized, suffix)
            };
        }

        private static string AppendSuffix(string baseName, string suffix) =>
            baseName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                ? baseName
                : baseName + suffix;

        private static string SingularizeIdentifier(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return value;

            if (value.EndsWith("ies", StringComparison.OrdinalIgnoreCase) && value.Length > 3)
                return value[..^3] + "y";

            if ((value.EndsWith("ses", StringComparison.OrdinalIgnoreCase)
                || value.EndsWith("xes", StringComparison.OrdinalIgnoreCase)
                || value.EndsWith("zes", StringComparison.OrdinalIgnoreCase)
                || value.EndsWith("ches", StringComparison.OrdinalIgnoreCase)
                || value.EndsWith("shes", StringComparison.OrdinalIgnoreCase))
                && value.Length > 2)
                return value[..^2];

            return value.EndsWith("s", StringComparison.OrdinalIgnoreCase) && value.Length > 1
                ? value[..^1]
                : value;
        }
    }
}
