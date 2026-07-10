using CodeMeridian.Core.GraphQueries;

namespace CodeMeridian.McpServer.GraphQl;

internal static class GraphQueryMappings
{
    internal static GraphNodeFilter ToFilter(this GraphNodeFilterInput? input)
    {
        return new GraphNodeFilter
        {
            Labels = input?.Labels ?? [],
            ProjectContext = input?.ProjectContext,
            PropertyEquals = ToDictionary(input?.PropertyEquals),
            PropertyContains = ToDictionary(input?.PropertyContains),
            NodeIds = input?.NodeIds ?? [],
            KeywordText = input?.KeywordText,
            KeywordCategory = input?.KeywordCategory
        };
    }

    internal static GraphRelationshipFilter ToFilter(this GraphRelationshipFilterInput? input)
    {
        return new GraphRelationshipFilter
        {
            RelationshipTypes = input?.RelationshipTypes ?? [],
            ProjectContext = input?.ProjectContext,
            PropertyEquals = ToDictionary(input?.PropertyEquals),
            PropertyContains = ToDictionary(input?.PropertyContains),
            FromNodeIds = input?.FromNodeIds ?? [],
            ToNodeIds = input?.ToNodeIds ?? []
        };
    }

    internal static GraphSort? ToSort(this GraphSortInput? input)
    {
        return input is null
            ? null
            : new GraphSort(input.Field, input.Direction);
    }

    private static IReadOnlyDictionary<string, string> ToDictionary(IReadOnlyList<GraphPropertyFilterInput>? filters)
    {
        if (filters is null || filters.Count == 0)
            return new Dictionary<string, string>(StringComparer.Ordinal);

        return filters.ToDictionary(filter => filter.Key, filter => filter.Value, StringComparer.Ordinal);
    }
}
