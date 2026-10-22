using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using CodeMeridian.Core.RepositoryConstraints;

namespace CodeMeridian.Core.CodeGraph;

public static class ArchitectureRuleSetConfiguration
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static ArchitectureRuleSet Parse(string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
        RejectDuplicateProperties(document.RootElement);
        var configuration = document.RootElement.Deserialize<Configuration>(Options)
            ?? throw new ArgumentException("An architecture configuration object is required.");
        if (document.RootElement.EnumerateObject().Any(property => property.Name.Equals("repositoryConstraints", StringComparison.OrdinalIgnoreCase)
            && property.Value.ValueKind == JsonValueKind.Null))
            throw new ArgumentException("An explicitly configured repository policy cannot be null.");
        if (configuration.Name is not null && !Text(configuration.Name)) throw new ArgumentException("The architecture name is invalid.");
        if (configuration.Layers is null || configuration.ForbiddenDependencies is null
            || configuration.Layers.Count > 100 || configuration.ForbiddenDependencies.Count > 100)
            throw new ArgumentException("Architecture collections must be non-null and contain at most 100 items.");
        var layers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var layer in configuration.Layers)
        {
            if (layer is null || !Text(layer.Id) || !layers.Add(layer.Id) || layer.NamespaceContainsAny is null
                || layer.NamespaceContainsAny.Count is 0 or > 32 || layer.NamespaceContainsAny.Any(pattern => !Text(pattern)))
                throw new ArgumentException("Architecture layers require unique IDs and nonempty namespace patterns.");
        }
        var rules = new List<ArchitectureForbiddenDependencyRule>();
        if (configuration.Layers.Count > 0 && configuration.ForbiddenDependencies.Count == 0)
            throw new ArgumentException("A namespace policy must declare forbidden dependencies.");
        foreach (var rule in configuration.ForbiddenDependencies)
        {
            if (rule is null || rule.From is null || rule.To is null || !layers.Contains(rule.From) || !layers.Contains(rule.To)
                || (rule.Reason is not null && !Text(rule.Reason)))
                throw new ArgumentException("Architecture dependencies must refer to declared layers and have valid reasons.");
            rules.Add(new(rule.From, rule.To, rule.Reason ?? $"{rule.From} must not depend on {rule.To}"));
        }
        if (configuration.RepositoryConstraints is { } policy && policy.Validate() is { Count: > 0 } diagnostics)
            throw new ArgumentException($"Invalid repository constraints at {diagnostics[0].Location}: {diagnostics[0].Reason}");
        if (configuration.RepositoryConstraints is null && configuration.Layers.Count == 0)
            throw new ArgumentException("An explicit namespace or repository-constraint policy is required.");
        return new(configuration.Name ?? "Configured Architecture", configuration.Layers, rules)
            { RepositoryConstraints = configuration.RepositoryConstraints };
    }

    // Indexed configuration entries are legacy bounded previews. Refuse potentially truncated
    // values rather than validating a different policy from the one the user configured.
    public static ArchitectureRuleSet ParseEntries(IReadOnlyDictionary<string, string> entries)
    {
        if (entries.Count is 0 or > 1000 || entries.Any(entry => entry.Value.Length >= 80))
            throw new ArgumentException("Architecture configuration is absent, oversized, or contains potentially truncated previews.");
        var values = entries.Select(entry => (Path: entry.Key.Split(':'), Value: entry.Value)).ToArray();
        if (values.Any(entry => entry.Path.Length > 16 || entry.Path.Any(string.IsNullOrWhiteSpace)))
            throw new ArgumentException("Architecture configuration paths are invalid.");
        return Parse(Build(values).ToJsonString());
    }

    private static JsonNode Build((string[] Path, string Value)[] entries)
    {
        var groups = entries.GroupBy(entry => entry.Path[0], StringComparer.OrdinalIgnoreCase).ToArray();
        JsonNode Value(IGrouping<string, (string[] Path, string Value)> group)
        {
            var items = group.ToArray();
            if (items.Any(item => item.Path.Length == 1))
            {
                if (items.Length != 1 || items[0].Path.Length != 1) throw new ArgumentException("Conflicting architecture configuration paths.");
                if (group.Key.Equals("requireLocalResolution", StringComparison.OrdinalIgnoreCase))
                {
                    if (!bool.TryParse(items[0].Value, out var required)) throw new ArgumentException("Reference resolution must be a boolean.");
                    return JsonValue.Create(required)!;
                }
                return JsonValue.Create(items[0].Value)!;
            }
            return Build(items.Select(item => (item.Path[1..], item.Value)).ToArray());
        }
        if (groups.All(group => int.TryParse(group.Key, out _)))
        {
            var ordered = groups.OrderBy(group => int.Parse(group.Key, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            if (ordered.Where((group, index) => group.Key != index.ToString(System.Globalization.CultureInfo.InvariantCulture)).Any())
                throw new ArgumentException("Architecture configuration arrays must have contiguous indexes.");
            return new JsonArray(ordered.Select(Value).ToArray());
        }
        var result = new JsonObject();
        foreach (var group in groups) result.Add(group.Key, Value(group));
        return result;
    }

    private static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new ArgumentException("Duplicate architecture configuration properties are invalid.");
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) RejectDuplicateProperties(item);
    }

    private static bool Text(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 512
        && value == value.Trim() && !value.Any(char.IsControl);

    private sealed record Configuration
    {
        [JsonPropertyName("$schema")] public string? Schema { get; init; }
        public string? Name { get; init; }
        public IReadOnlyList<ArchitectureLayerDefinition> Layers { get; init; } = [];
        public IReadOnlyList<Dependency> ForbiddenDependencies { get; init; } = [];
        public RepositoryConstraintPolicy? RepositoryConstraints { get; init; }
    }

    private sealed record Dependency(string From, string To, string? Reason);
}
