namespace CodeMeridian.Application.ClientExtensions;

public sealed record ClientExtensionContract(
    string Version,
    string ExtensionOwnership,
    string GraphQlEndpointPath,
    string AuthRequirement,
    IReadOnlyList<string> SupportedAuthHeaders,
    IReadOnlyList<string> SupportedAuthHeaderFormats,
    int DefaultPageSize,
    int MaxPageSize,
    int MaxTraversalDepth,
    int MaxAllowedFields,
    int MaxAllowedRecursionDepth,
    int ExecutionTimeoutSeconds,
    IReadOnlyList<string> SupportedNodeSortFields,
    IReadOnlyList<string> SupportedRelationshipSortFields,
    IReadOnlyList<string> DocumentationPaths,
    IReadOnlyList<string> ExampleIds);
