using CodeMeridian.Application.GraphQueries;
using CodeMeridian.Core.GraphQueries;
using HotChocolate;
using HotChocolate.Authorization;

namespace CodeMeridian.McpServer.GraphQl;

[Authorize]
public sealed class GraphQueryType
{
    public Task<IReadOnlyList<string>> GetLabels(
        [Service] IGraphQueryService queryService,
        CancellationToken cancellationToken)
    {
        return queryService.ListLabelsAsync(cancellationToken);
    }

    public Task<IReadOnlyList<string>> GetRelationshipTypes(
        [Service] IGraphQueryService queryService,
        CancellationToken cancellationToken)
    {
        return queryService.ListRelationshipTypesAsync(cancellationToken);
    }

    public Task<GraphNode?> GetNode(
        string id,
        [Service] IGraphQueryService queryService,
        CancellationToken cancellationToken)
    {
        return queryService.GetNodeAsync(id, cancellationToken);
    }

    public Task<IReadOnlyList<GraphNode>> GetNodes(
        [Service] IGraphQueryService queryService,
        CancellationToken cancellationToken,
        GraphNodeFilterInput? filter = null,
        GraphSortInput? sort = null,
        int skip = 0,
        int limit = 25)
    {
        return queryService.QueryNodesAsync(
            filter.ToFilter(),
            sort.ToSort(),
            skip,
            limit,
            cancellationToken);
    }

    public Task<IReadOnlyList<GraphRelationship>> GetRelationships(
        [Service] IGraphQueryService queryService,
        CancellationToken cancellationToken,
        GraphRelationshipFilterInput? filter = null,
        GraphSortInput? sort = null,
        int skip = 0,
        int limit = 25)
    {
        return queryService.QueryRelationshipsAsync(
            filter.ToFilter(),
            sort.ToSort(),
            skip,
            limit,
            cancellationToken);
    }

    public Task<IReadOnlyList<GraphNeighbor>> GetNeighbors(
        string nodeId,
        [Service] IGraphQueryService queryService,
        CancellationToken cancellationToken,
        IReadOnlyList<string>? relationshipTypes = null,
        GraphDirection direction = GraphDirection.Both,
        int depth = 1,
        int limit = 25)
    {
        return queryService.GetNeighborsAsync(
            nodeId,
            relationshipTypes,
            direction,
            depth,
            limit,
            cancellationToken);
    }
}
