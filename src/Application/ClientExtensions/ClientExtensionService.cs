using System.Text;
using CodeMeridian.Application.GraphQueries;

namespace CodeMeridian.Application.ClientExtensions;

public sealed class ClientExtensionService : IClientExtensionService
{
    private const string ResourcePrefix = "CodeMeridian.Application.ClientExtensions.Examples.";

    private static readonly ExampleDefinition[] ExampleDefinitions =
    [
        new(
            "schema-overview",
            "Schema overview",
            "Inspect labels and relationship types before composing narrower GraphQL queries.",
            "Discover the graph shape and available relationship families.",
            "01-schema-overview.graphql",
            "Top-level `labels[]` and `relationshipTypes[]` arrays.",
            [
                "Use this first when a client extension needs to learn the graph vocabulary for a project."
            ]),
        new(
            "project-code-nodes",
            "Project code nodes",
            "List bounded code-node results for one project context.",
            "Start a client-owned behavior from a project-scoped node slice.",
            "02-project-code-nodes.graphql",
            "A `nodes[]` array with common code-node fields such as id, name, type, path, and project.",
            [
                "Use projectContext filters to keep exploratory behaviors bounded."
            ]),
        new(
            "keyword-search",
            "Keyword search",
            "Search keyword nodes by their stored value or normalized value.",
            "Drive lightweight lexical lookup before asking broader graph questions.",
            "03-keyword-search.graphql",
            "A `nodes[]` array with keyword metadata and properties.",
            [
                "Useful for client behaviors that pivot from terminology into graph reads."
            ]),
        new(
            "node-deep-dive",
            "Node deep dive",
            "Fetch one node plus selected relationships and properties.",
            "Inspect a single canonical node before composing follow-up queries.",
            "04-node-deep-dive.graphql",
            "A single `node` object with labels, properties, and bounded relationship collections.",
            [
                "Use canonical node IDs from CodeMeridian tools or prior GraphQL results."
            ]),
        new(
            "neighborhood-walk",
            "Neighborhood walk",
            "Expand a bounded neighborhood around one seed node.",
            "Build client-side behaviors that traverse a small graph slice without unbounded fan-out.",
            "05-neighborhood-walk.graphql",
            "A `neighbors[]` array containing distance, relationship, and node payloads.",
            [
                "Traversal depth is clamped server-side to keep client extensions deterministic."
            ]),
        new(
            "relationships-by-type",
            "Relationships by type",
            "Query relationships directly by type and endpoint IDs.",
            "Audit graph edges before a client composes higher-level summaries.",
            "06-relationships-by-type.graphql",
            "A `relationships[]` array with ids, types, endpoints, and properties.",
            [
                "Use relationship filters when the client already knows the edge family it cares about."
            ]),
        new(
            "file-path-search",
            "File path search",
            "Find code nodes under a bounded file path prefix.",
            "Anchor a client behavior to one subsystem or folder before exploring further.",
            "07-file-path-search.graphql",
            "A `nodes[]` array filtered by file path and project context.",
            [
                "Useful for repo-localized client behaviors such as focused code exploration."
            ]),
        new(
            "keyword-category",
            "Keyword category",
            "Slice keyword nodes by saved classification metadata when available.",
            "Build client-side taxonomy or discovery behaviors on top of keyword classification.",
            "08-keyword-category.graphql",
            "A `nodes[]` array with keyword properties for the selected category.",
            [
                "This example is most useful after keyword classification metadata exists in the graph."
            ])
    ];

    public ClientExtensionContract GetContract()
    {
        return new ClientExtensionContract(
            Version: "v1",
            ExtensionOwnership: "CodeMeridian exposes facts, schema, auth, and bounded graph access. Clients own prompts, routing, UI, query composition, and behavior chaining.",
            GraphQlEndpointPath: GraphQlReadContract.EndpointPath,
            AuthRequirement: "GraphQL query execution requires the same API key as the REST and MCP surfaces, even though the browser UI can load without auth.",
            SupportedAuthHeaders: GraphQlReadContract.SupportedAuthHeaders,
            SupportedAuthHeaderFormats: GraphQlReadContract.SupportedAuthHeaderFormats,
            DefaultPageSize: GraphQlReadContract.DefaultPageSize,
            MaxPageSize: GraphQlReadContract.MaxPageSize,
            MaxTraversalDepth: GraphQlReadContract.MaxTraversalDepth,
            MaxAllowedFields: GraphQlReadContract.MaxAllowedFields,
            MaxAllowedRecursionDepth: GraphQlReadContract.MaxAllowedRecursionDepth,
            ExecutionTimeoutSeconds: (int)GraphQlReadContract.ExecutionTimeout.TotalSeconds,
            SupportedNodeSortFields: GraphQlReadContract.SupportedNodeSortFields,
            SupportedRelationshipSortFields: GraphQlReadContract.SupportedRelationshipSortFields,
            DocumentationPaths:
            [
                "docs/graphql/README.md",
                "docs/installation.md",
                "docs/context-workflows.md"
            ],
            ExampleIds: ExampleDefinitions.Select(definition => definition.Id).ToArray());
    }

    public IReadOnlyList<ClientExtensionExample> ListExamples() =>
        ExampleDefinitions.Select(BuildExample).ToArray();

    public ClientExtensionExample? GetExample(string exampleId)
    {
        if (string.IsNullOrWhiteSpace(exampleId))
            return null;

        var definition = ExampleDefinitions.FirstOrDefault(definition =>
            definition.Id.Equals(exampleId.Trim(), StringComparison.OrdinalIgnoreCase));

        return definition is null ? null : BuildExample(definition);
    }

    private static ClientExtensionExample BuildExample(ExampleDefinition definition)
    {
        var document = ReadEmbeddedDocument(definition.FileName);
        return new ClientExtensionExample(
            Id: definition.Id,
            Name: definition.Name,
            Description: definition.Description,
            Goal: definition.Goal,
            GraphQlDocumentPath: $"docs/graphql/{definition.FileName}",
            GraphQlDocument: document,
            VariablesTemplate: ExtractVariablesTemplate(document),
            ExpectedResultShape: definition.ExpectedResultShape,
            Notes: definition.Notes);
    }

    private static string ReadEmbeddedDocument(string fileName)
    {
        var assembly = typeof(ClientExtensionService).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourcePrefix + fileName);
        if (stream is null)
            throw new InvalidOperationException($"Embedded GraphQL example '{fileName}' was not found.");

        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static string? ExtractVariablesTemplate(string document)
    {
        var lines = document.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var builder = new StringBuilder();
        var collecting = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();
            if (!collecting)
            {
                if (line.Equals("# Nitro variables:", StringComparison.Ordinal))
                    collecting = true;

                continue;
            }

            if (!line.TrimStart().StartsWith('#'))
                break;

            builder.AppendLine(line.TrimStart()[1..].TrimStart());
        }

        var template = builder.ToString().TrimEnd();
        return string.IsNullOrWhiteSpace(template) ? null : template;
    }

    private sealed record ExampleDefinition(
        string Id,
        string Name,
        string Description,
        string Goal,
        string FileName,
        string ExpectedResultShape,
        IReadOnlyList<string> Notes);
}
