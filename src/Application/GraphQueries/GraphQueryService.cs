using CodeMeridian.Core.GraphQueries;

namespace CodeMeridian.Application.GraphQueries;

public sealed class GraphQueryService : IGraphQueryService
{
    private readonly IGraphReadRepository _repository;

    public GraphQueryService(IGraphReadRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<string>> ListLabelsAsync(CancellationToken cancellationToken = default)
    {
        var labels = await _repository.ListLabelsAsync(cancellationToken);
        return labels
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(label => label, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<IReadOnlyList<string>> ListRelationshipTypesAsync(CancellationToken cancellationToken = default)
    {
        var relationshipTypes = await _repository.ListRelationshipTypesAsync(cancellationToken);
        return relationshipTypes
            .Where(type => !string.IsNullOrWhiteSpace(type))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(type => type, StringComparer.Ordinal)
            .ToArray();
    }

    public Task<GraphNode?> GetNodeAsync(string nodeId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
            throw new ArgumentException("A non-empty nodeId is required.", nameof(nodeId));

        return _repository.GetNodeAsync(nodeId.Trim(), cancellationToken);
    }

    public Task<IReadOnlyList<GraphNode>> QueryNodesAsync(
        GraphNodeFilter filter,
        GraphSort? sort = null,
        int skip = 0,
        int limit = 25,
        CancellationToken cancellationToken = default)
    {
        ValidatePagination(skip, limit);
        ValidateSort(sort, GraphQlReadContract.SupportedNodeSortFields);

        return _repository.QueryNodesAsync(
            Normalize(filter),
            sort,
            skip,
            Math.Min(limit, GraphQlReadContract.MaxPageSize),
            cancellationToken);
    }

    public Task<IReadOnlyList<GraphRelationship>> QueryRelationshipsAsync(
        GraphRelationshipFilter filter,
        GraphSort? sort = null,
        int skip = 0,
        int limit = 25,
        CancellationToken cancellationToken = default)
    {
        ValidatePagination(skip, limit);
        ValidateSort(sort, GraphQlReadContract.SupportedRelationshipSortFields);

        return _repository.QueryRelationshipsAsync(
            Normalize(filter),
            sort,
            skip,
            Math.Min(limit, GraphQlReadContract.MaxPageSize),
            cancellationToken);
    }

    public Task<IReadOnlyList<GraphNeighbor>> GetNeighborsAsync(
        string nodeId,
        IReadOnlyCollection<string>? relationshipTypes = null,
        GraphDirection direction = GraphDirection.Both,
        int depth = 1,
        int limit = 25,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
            throw new ArgumentException("A non-empty nodeId is required.", nameof(nodeId));

        ValidatePagination(skip: 0, limit);

        return _repository.GetNeighborsAsync(
            nodeId.Trim(),
            relationshipTypes?
                .Where(type => !string.IsNullOrWhiteSpace(type))
                .Distinct(StringComparer.Ordinal)
                .ToArray()
            ?? [],
            direction,
            ClampDepth(depth),
            Math.Min(limit, GraphQlReadContract.MaxPageSize),
            cancellationToken);
    }

    private static void ValidatePagination(int skip, int limit)
    {
        if (skip < 0)
            throw new ArgumentOutOfRangeException(nameof(skip), "Skip must be zero or greater.");

        if (limit <= 0)
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be greater than zero.");
    }

    private static void ValidateSort(GraphSort? sort, IReadOnlyCollection<string> allowedFields)
    {
        if (sort is null)
            return;

        if (!allowedFields.Contains(sort.Field))
        {
            throw new ArgumentOutOfRangeException(
                nameof(sort),
                sort.Field,
                $"Unsupported sort field '{sort.Field}'. Allowed values: {string.Join(", ", allowedFields.OrderBy(value => value, StringComparer.Ordinal))}.");
        }
    }

    private static int ClampDepth(int depth)
    {
        if (depth <= 0)
            throw new ArgumentOutOfRangeException(nameof(depth), "Depth must be greater than zero.");

        return Math.Min(depth, GraphQlReadContract.MaxTraversalDepth);
    }

    private static GraphNodeFilter Normalize(GraphNodeFilter filter)
    {
        return filter with
        {
            Labels = filter.Labels
                .Where(label => !string.IsNullOrWhiteSpace(label))
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            PropertyEquals = filter.PropertyEquals
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
                .ToDictionary(pair => pair.Key.Trim(), pair => pair.Value, StringComparer.Ordinal),
            PropertyContains = filter.PropertyContains
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
                .ToDictionary(pair => pair.Key.Trim(), pair => pair.Value, StringComparer.Ordinal),
            NodeIds = filter.NodeIds
                .Where(nodeId => !string.IsNullOrWhiteSpace(nodeId))
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            ProjectContext = string.IsNullOrWhiteSpace(filter.ProjectContext) ? null : filter.ProjectContext.Trim(),
            KeywordText = string.IsNullOrWhiteSpace(filter.KeywordText) ? null : filter.KeywordText.Trim(),
            KeywordCategory = string.IsNullOrWhiteSpace(filter.KeywordCategory) ? null : filter.KeywordCategory.Trim()
        };
    }

    private static GraphRelationshipFilter Normalize(GraphRelationshipFilter filter)
    {
        return filter with
        {
            RelationshipTypes = filter.RelationshipTypes
                .Where(type => !string.IsNullOrWhiteSpace(type))
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            PropertyEquals = filter.PropertyEquals
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
                .ToDictionary(pair => pair.Key.Trim(), pair => pair.Value, StringComparer.Ordinal),
            PropertyContains = filter.PropertyContains
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
                .ToDictionary(pair => pair.Key.Trim(), pair => pair.Value, StringComparer.Ordinal),
            FromNodeIds = filter.FromNodeIds
                .Where(nodeId => !string.IsNullOrWhiteSpace(nodeId))
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            ToNodeIds = filter.ToNodeIds
                .Where(nodeId => !string.IsNullOrWhiteSpace(nodeId))
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            ProjectContext = string.IsNullOrWhiteSpace(filter.ProjectContext) ? null : filter.ProjectContext.Trim()
        };
    }
}
