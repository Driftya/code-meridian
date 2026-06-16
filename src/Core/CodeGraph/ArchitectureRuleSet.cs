namespace CodeMeridian.Core.CodeGraph;

public sealed record ArchitectureRuleSet(
    string Name,
    IReadOnlyList<ArchitectureLayerDefinition> Layers,
    IReadOnlyList<ArchitectureForbiddenDependencyRule> ForbiddenDependencies);

public sealed record ArchitectureLayerDefinition(
    string Id,
    IReadOnlyList<string> NamespaceContainsAny);

public sealed record ArchitectureForbiddenDependencyRule(
    string FromLayerId,
    string ToLayerId,
    string Reason);
