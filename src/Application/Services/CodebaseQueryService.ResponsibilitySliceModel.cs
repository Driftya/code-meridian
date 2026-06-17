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

        if (ContainsAny(text, "context", "minimal", "editing", "pack", "token"))
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
        return cleaned.EndsWith("s", StringComparison.OrdinalIgnoreCase) ? cleaned : cleaned + "s";
    }

    private static string ToPascalPlural(string value)
    {
        var cleaned = CleanName(value);
        if (string.IsNullOrWhiteSpace(cleaned))
            return "Deferred";

        return cleaned.EndsWith("s", StringComparison.OrdinalIgnoreCase) ? cleaned : cleaned + "s";
    }

    private static string CleanName(string value)
    {
        var words = SplitIdentifier(RemoveSuffix(RemovePrefix(value, "I"), "Async"))
            .Where(word => !GenericMethodTerms.Contains(word, StringComparer.OrdinalIgnoreCase))
            .Take(3)
            .ToArray();

        return words.Length == 0
            ? value
            : string.Concat(words.Select(word => char.ToUpperInvariant(word[0]) + word[1..]));
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
        string Reason)
    {
        public static ResponsibilitySlice Create(
            string name,
            IReadOnlyList<ResponsibilityMethodSignals> methods,
            IReadOnlyList<string> docSources)
        {
            var dependencies = methods.SelectMany(method => method.Dependencies).DistinctBy(node => node.Id).ToArray();
            var callers = methods.SelectMany(method => method.WorkflowCallers).DistinctBy(node => node.Id).ToArray();
            var tests = methods.SelectMany(method => method.RelatedTests).DistinctBy(node => node.Id).ToArray();
            var score = methods.Count
                        + dependencies.Length * 5
                        + callers.Length * 4
                        + tests.Length * 4
                        + Math.Min(docSources.Count, 3) * 2;

            if (methods.All(method => method.Dependencies.Count == 0 && method.WorkflowCallers.Count == 0 && method.RelatedTests.Count == 0))
                score -= 3;

            var confidence = score >= 18 ? "High" : score >= 9 ? "Medium" : "Low";
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

            return new ResponsibilitySlice(
                name,
                BuildServiceName(name),
                methods,
                tests,
                score,
                confidence,
                string.Join(", ", evidence));
        }

        private static string BuildServiceName(string name)
        {
            var normalized = name.EndsWith("s", StringComparison.OrdinalIgnoreCase) ? name[..^1] : name;
            return normalized switch
            {
                "ContextPack" => "ContextPackBuilderService",
                "Impact" => "ImpactAnalysisService",
                "Diagnostic" => "CodeDiagnosticsQueryService",
                "Knowledge" => "RelatedKnowledgeQueryService",
                "Search" => "CodebaseSearchService",
                "Configuration" => "ConfigurationQueryService",
                _ => normalized + "Service"
            };
        }
    }
}
