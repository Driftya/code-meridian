namespace CodeMeridian.Application.GraphQueries;

public static class GraphQlReadContract
{
    public const string EndpointPath = "/graphql";
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 100;
    public const int MaxTraversalDepth = 3;
    public const int MaxAllowedFields = 256;
    public const int MaxAllowedRecursionDepth = 32;

    public static TimeSpan ExecutionTimeout => TimeSpan.FromSeconds(10);

    public static IReadOnlyList<string> SupportedAuthHeaders { get; } =
    [
        "Authorization",
        "X-CodeMeridian-ApiKey"
    ];

    public static IReadOnlyList<string> SupportedAuthHeaderFormats { get; } =
    [
        "Authorization: Bearer <your-api-key>",
        "X-CodeMeridian-ApiKey: <your-api-key>"
    ];

    public static IReadOnlyList<string> SupportedNodeSortFields { get; } =
    [
        "id",
        "name",
        "projectContext",
        "primaryLabel",
        "type",
        "filePath"
    ];

    public static IReadOnlyList<string> SupportedRelationshipSortFields { get; } =
    [
        "id",
        "type",
        "fromNodeId",
        "toNodeId"
    ];
}
