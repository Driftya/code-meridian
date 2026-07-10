namespace CodeMeridian.Core.GraphQueries;

public interface IGraphReadRepository
{
    Task<IReadOnlyList<string>> ListLabelsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ListRelationshipTypesAsync(CancellationToken cancellationToken = default);
    Task<GraphNode?> GetNodeAsync(string nodeId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GraphNode>> QueryNodesAsync(
        GraphNodeFilter filter,
        GraphSort? sort,
        int skip,
        int limit,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GraphRelationship>> QueryRelationshipsAsync(
        GraphRelationshipFilter filter,
        GraphSort? sort,
        int skip,
        int limit,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GraphNeighbor>> GetNeighborsAsync(
        string nodeId,
        IReadOnlyCollection<string> relationshipTypes,
        GraphDirection direction,
        int depth,
        int limit,
        CancellationToken cancellationToken = default);
}
