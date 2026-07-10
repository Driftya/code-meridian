using System.Text.RegularExpressions;
using CodeMeridian.Core.GraphQueries;

namespace CodeMeridian.Infrastructure.GraphQueries;

internal static class Neo4jGraphQueryBuilder
{
    private static readonly Regex SafePropertyNamePattern = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

    private static readonly IReadOnlyDictionary<string, string> NodeSortExpressions =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["id"] = "coalesce(n.id, elementId(n))",
            ["name"] = "coalesce(n.name, n.source, n.value, '')",
            ["projectContext"] = "coalesce(n.projectContext, '')",
            ["primaryLabel"] = "head(labels(n))",
            ["type"] = "coalesce(n.type, head(labels(n)), '')",
            ["filePath"] = "coalesce(n.filePath, '')"
        };

    private static readonly IReadOnlyDictionary<string, string> RelationshipSortExpressions =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["id"] = "coalesce(r.id, elementId(r))",
            ["type"] = "type(r)",
            ["fromNodeId"] = "coalesce(`from`.id, elementId(`from`))",
            ["toNodeId"] = "coalesce(`to`.id, elementId(`to`))"
        };

    internal static Neo4jGraphQuerySpec BuildNodeQuery(
        GraphNodeFilter filter,
        GraphSort? sort,
        int skip,
        int limit)
    {
        var conditions = new List<string>();
        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["skip"] = skip,
            ["limit"] = limit
        };

        if (filter.Labels.Count > 0)
        {
            conditions.Add("ANY(label IN labels(n) WHERE label IN $labels)");
            parameters["labels"] = filter.Labels.ToArray();
        }

        if (filter.NodeIds.Count > 0)
        {
            conditions.Add("coalesce(n.id, elementId(n)) IN $nodeIds");
            parameters["nodeIds"] = filter.NodeIds.ToArray();
        }

        if (!string.IsNullOrWhiteSpace(filter.ProjectContext))
        {
            conditions.Add("toLower(coalesce(n.projectContext, '')) = $projectContextNormalized");
            parameters["projectContextNormalized"] = filter.ProjectContext!.Trim().ToLowerInvariant();
        }

        AppendPropertyFilters("n", filter.PropertyEquals, filter.PropertyContains, conditions, parameters);

        if (!string.IsNullOrWhiteSpace(filter.KeywordText))
        {
            conditions.Add("(toLower(coalesce(n.value, '')) CONTAINS $keywordText OR toLower(coalesce(n.normalizedValue, '')) CONTAINS $keywordText)");
            parameters["keywordText"] = filter.KeywordText!.Trim().ToLowerInvariant();
        }

        if (!string.IsNullOrWhiteSpace(filter.KeywordCategory))
        {
            conditions.Add("toLower(coalesce(n.classification, coalesce(n.keywordCategory, ''))) = $keywordCategory");
            parameters["keywordCategory"] = filter.KeywordCategory!.Trim().ToLowerInvariant();
        }

        var whereClause = conditions.Count == 0
            ? string.Empty
            : $"WHERE {string.Join(" AND ", conditions)}";
        var orderByClause = BuildOrderBy(sort, NodeSortExpressions, "coalesce(n.id, elementId(n))");

        return new Neo4jGraphQuerySpec(
            $"""
            MATCH (n)
            {whereClause}
            RETURN n, labels(n) AS nodeLabels
            {orderByClause}
            SKIP $skip
            LIMIT $limit
            """,
            parameters);
    }

    internal static Neo4jGraphQuerySpec BuildRelationshipQuery(
        GraphRelationshipFilter filter,
        GraphSort? sort,
        int skip,
        int limit)
    {
        var conditions = new List<string>();
        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["skip"] = skip,
            ["limit"] = limit
        };

        if (filter.RelationshipTypes.Count > 0)
        {
            conditions.Add("type(r) IN $relationshipTypes");
            parameters["relationshipTypes"] = filter.RelationshipTypes.ToArray();
        }

        if (filter.FromNodeIds.Count > 0)
        {
            conditions.Add("coalesce(`from`.id, elementId(`from`)) IN $fromNodeIds");
            parameters["fromNodeIds"] = filter.FromNodeIds.ToArray();
        }

        if (filter.ToNodeIds.Count > 0)
        {
            conditions.Add("coalesce(`to`.id, elementId(`to`)) IN $toNodeIds");
            parameters["toNodeIds"] = filter.ToNodeIds.ToArray();
        }

        if (!string.IsNullOrWhiteSpace(filter.ProjectContext))
        {
            conditions.Add("(toLower(coalesce(`from`.projectContext, '')) = $projectContextNormalized OR toLower(coalesce(`to`.projectContext, '')) = $projectContextNormalized)");
            parameters["projectContextNormalized"] = filter.ProjectContext!.Trim().ToLowerInvariant();
        }

        AppendPropertyFilters("r", filter.PropertyEquals, filter.PropertyContains, conditions, parameters);

        var whereClause = conditions.Count == 0
            ? string.Empty
            : $"WHERE {string.Join(" AND ", conditions)}";
        var orderByClause = BuildOrderBy(sort, RelationshipSortExpressions, "coalesce(r.id, elementId(r))");

        return new Neo4jGraphQuerySpec(
            $"""
            MATCH (`from`)-[r]->(`to`)
            {whereClause}
            RETURN
                r,
                coalesce(`from`.id, elementId(`from`)) AS fromNodeId,
                coalesce(`to`.id, elementId(`to`)) AS toNodeId
            {orderByClause}
            SKIP $skip
            LIMIT $limit
            """,
            parameters);
    }

    private static void AppendPropertyFilters(
        string variableName,
        IReadOnlyDictionary<string, string> propertyEquals,
        IReadOnlyDictionary<string, string> propertyContains,
        ICollection<string> conditions,
        IDictionary<string, object?> parameters)
    {
        var propertyIndex = 0;

        foreach (var pair in propertyEquals)
        {
            var propertyName = ValidatePropertyName(pair.Key);
            var parameterName = $"propertyEquals{propertyIndex++}";
            conditions.Add($"toString(coalesce({variableName}.{propertyName}, '')) = ${parameterName}");
            parameters[parameterName] = pair.Value;
        }

        foreach (var pair in propertyContains)
        {
            var propertyName = ValidatePropertyName(pair.Key);
            var parameterName = $"propertyContains{propertyIndex++}";
            conditions.Add($"toLower(toString(coalesce({variableName}.{propertyName}, ''))) CONTAINS ${parameterName}");
            parameters[parameterName] = pair.Value.ToLowerInvariant();
        }
    }

    private static string ValidatePropertyName(string propertyName)
    {
        if (!SafePropertyNamePattern.IsMatch(propertyName))
            throw new ArgumentOutOfRangeException(nameof(propertyName), propertyName, "Only simple Neo4j property names are supported.");

        return propertyName;
    }

    private static string BuildOrderBy(
        GraphSort? sort,
        IReadOnlyDictionary<string, string> expressions,
        string fallbackExpression)
    {
        if (sort is null)
            return $"ORDER BY {fallbackExpression}";

        var expression = expressions.TryGetValue(sort.Field, out var mappedExpression)
            ? mappedExpression
            : throw new ArgumentOutOfRangeException(nameof(sort), sort.Field, $"Unsupported sort field '{sort.Field}'.");
        var direction = sort.Direction == GraphSortDirection.Descending
            ? "DESC"
            : "ASC";

        return $"ORDER BY {expression} {direction}, {fallbackExpression}";
    }
}
