namespace CodeMeridian.Application.ClientExtensions;

public sealed record ClientExtensionExample(
    string Id,
    string Name,
    string Description,
    string Goal,
    string GraphQlDocumentPath,
    string GraphQlDocument,
    string? VariablesTemplate,
    string ExpectedResultShape,
    IReadOnlyList<string> Notes);
