using System.ComponentModel;
using System.Text;
using CodeMeridian.Application.ClientExtensions;
using ModelContextProtocol.Server;

namespace CodeMeridian.McpServer.Tools;

/// <summary>
/// Read-only discovery tools for client-owned behaviors that query CodeMeridian's GraphQL surface.
/// </summary>
[McpServerToolType]
public sealed class ClientExtensionTools(IClientExtensionService clientExtensions)
{
    [McpServerTool(Name = "get_client_extension_contract")]
    [Description(
        "Return the canonical contract for client-owned extensions that query CodeMeridian through GraphQL. " +
        "Use this before building custom routing, prompts, UI, or query composition on the client.")]
    public string GetClientExtensionContract()
    {
        var contract = clientExtensions.GetContract();
        var builder = new StringBuilder();
        builder.AppendLine("# Client Extension Contract");
        builder.AppendLine();
        builder.AppendLine($"- Contract version: `{contract.Version}`");
        builder.AppendLine($"- GraphQL endpoint: `{contract.GraphQlEndpointPath}`");
        builder.AppendLine($"- Client ownership: {contract.ExtensionOwnership}");
        builder.AppendLine($"- Auth requirement: {contract.AuthRequirement}");
        builder.AppendLine($"- Supported auth headers: {string.Join(", ", contract.SupportedAuthHeaders)}");
        builder.AppendLine("- Supported auth header formats:");
        foreach (var format in contract.SupportedAuthHeaderFormats)
            builder.AppendLine($"  - `{format}`");

        builder.AppendLine("- Query limits:");
        builder.AppendLine($"  - Default page size: {contract.DefaultPageSize}");
        builder.AppendLine($"  - Max page size: {contract.MaxPageSize}");
        builder.AppendLine($"  - Max traversal depth: {contract.MaxTraversalDepth}");
        builder.AppendLine($"  - Max allowed fields: {contract.MaxAllowedFields}");
        builder.AppendLine($"  - Max recursion depth: {contract.MaxAllowedRecursionDepth}");
        builder.AppendLine($"  - Execution timeout: {contract.ExecutionTimeoutSeconds}s");
        builder.AppendLine($"- Supported node sort fields: {string.Join(", ", contract.SupportedNodeSortFields)}");
        builder.AppendLine($"- Supported relationship sort fields: {string.Join(", ", contract.SupportedRelationshipSortFields)}");
        builder.AppendLine("- Documentation:");
        foreach (var path in contract.DocumentationPaths)
            builder.AppendLine($"  - `{path}`");

        builder.AppendLine("- Example ids:");
        foreach (var exampleId in contract.ExampleIds)
            builder.AppendLine($"  - `{exampleId}`");

        return builder.ToString().TrimEnd();
    }

    [McpServerTool(Name = "list_client_extension_examples")]
    [Description(
        "List curated GraphQL examples that client-side extensions can reuse as deterministic starting points. " +
        "These examples are checked into docs/graphql and do not execute arbitrary client code on the server.")]
    public string ListClientExtensionExamples()
    {
        var examples = clientExtensions.ListExamples();
        var builder = new StringBuilder();
        builder.AppendLine("# Client Extension Examples");
        builder.AppendLine();

        foreach (var example in examples)
        {
            builder.AppendLine($"- `{example.Id}`");
            builder.AppendLine($"  Name: {example.Name}");
            builder.AppendLine($"  Goal: {example.Goal}");
            builder.AppendLine($"  Path: `{example.GraphQlDocumentPath}`");
            builder.AppendLine($"  Description: {example.Description}");
        }

        return builder.ToString().TrimEnd();
    }

    [McpServerTool(Name = "get_client_extension_example")]
    [Description(
        "Return one curated GraphQL example, including the checked-in document, variables template, expected result shape, and usage notes. " +
        "Use the example id returned by list_client_extension_examples.")]
    public string GetClientExtensionExample(
        [Description("Stable example id from list_client_extension_examples, e.g. 'keyword-search'")]
        string exampleId)
    {
        var example = clientExtensions.GetExample(exampleId);
        if (example is null)
            return $"Unknown client extension example '{exampleId}'. Use list_client_extension_examples to see valid ids.";

        var builder = new StringBuilder();
        builder.AppendLine($"# Client Extension Example: {example.Id}");
        builder.AppendLine();
        builder.AppendLine($"- Name: {example.Name}");
        builder.AppendLine($"- Goal: {example.Goal}");
        builder.AppendLine($"- Path: `{example.GraphQlDocumentPath}`");
        builder.AppendLine($"- Expected result shape: {example.ExpectedResultShape}");
        if (!string.IsNullOrWhiteSpace(example.VariablesTemplate))
        {
            builder.AppendLine("- Variables template:");
            builder.AppendLine("```json");
            builder.AppendLine(example.VariablesTemplate);
            builder.AppendLine("```");
        }

        if (example.Notes.Count > 0)
        {
            builder.AppendLine("- Notes:");
            foreach (var note in example.Notes)
                builder.AppendLine($"  - {note}");
        }

        builder.AppendLine("```graphql");
        builder.AppendLine(example.GraphQlDocument.TrimEnd());
        builder.AppendLine("```");
        return builder.ToString().TrimEnd();
    }
}
