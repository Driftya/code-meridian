using CodeMeridian.Core.GraphQueries;

namespace CodeMeridian.Application.GraphQueries;

public interface IGraphQueryService
{
    Task<IReadOnlyList<string>> ListLabelsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ListRelationshipTypesAsync(CancellationToken cancellationToken = default);
    Task<GraphNode?> GetNodeAsync(string nodeId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GraphNode>> QueryNodesAsync(
        GraphNodeFilter filter,
        GraphSort? sort = null,
        int skip = 0,
        int limit = 25,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GraphRelationship>> QueryRelationshipsAsync(
        GraphRelationshipFilter filter,
        GraphSort? sort = null,
        int skip = 0,
        int limit = 25,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GraphNeighbor>> GetNeighborsAsync(
        string nodeId,
        IReadOnlyCollection<string>? relationshipTypes = null,
        GraphDirection direction = GraphDirection.Both,
        int depth = 1,
        int limit = 25,
        CancellationToken cancellationToken = default);
}
