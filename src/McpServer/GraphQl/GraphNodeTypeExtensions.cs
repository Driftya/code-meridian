using CodeMeridian.Application.GraphQueries;
using CodeMeridian.Core.GraphQueries;
using HotChocolate;
using HotChocolate.Types;

namespace CodeMeridian.McpServer.GraphQl;

[ExtendObjectType<GraphNode>]
public sealed class GraphNodeTypeExtensions
{
    public Task<IReadOnlyList<GraphRelationship>> GetOutgoingRelationships(
        [Parent] GraphNode node,
        [Service] IGraphQueryService queryService,
        CancellationToken cancellationToken,
        IReadOnlyList<string>? relationshipTypes = null,
        int limit = 25)
    {
        return queryService.QueryRelationshipsAsync(
            new GraphRelationshipFilter
            {
                FromNodeIds = [node.Id],
                RelationshipTypes = relationshipTypes ?? []
            },
            sort: null,
            skip: 0,
            limit,
            cancellationToken);
    }

    public Task<IReadOnlyList<GraphRelationship>> GetIncomingRelationships(
        [Parent] GraphNode node,
        [Service] IGraphQueryService queryService,
        CancellationToken cancellationToken,
        IReadOnlyList<string>? relationshipTypes = null,
        int limit = 25)
    {
        return queryService.QueryRelationshipsAsync(
            new GraphRelationshipFilter
            {
                ToNodeIds = [node.Id],
                RelationshipTypes = relationshipTypes ?? []
            },
            sort: null,
            skip: 0,
            limit,
            cancellationToken);
    }
}
