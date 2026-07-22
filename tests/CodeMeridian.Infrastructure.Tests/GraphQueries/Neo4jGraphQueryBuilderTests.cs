using CodeMeridian.Core.GraphQueries;
using CodeMeridian.Infrastructure.GraphQueries;
using FluentAssertions;

namespace CodeMeridian.Infrastructure.Tests.GraphQueries;

public sealed class Neo4jGraphQueryBuilderTests
{
    [Fact]
    public void BuildNodeQuery_IncludesAllConfiguredFiltersAndAscendingSort()
    {
        var query = Neo4jGraphQueryBuilder.BuildNodeQuery(
            new GraphNodeFilter
            {
                Labels = ["Method", "Class"],
                NodeIds = ["node-1"],
                ProjectContext = "  CodeMeridian ",
                PropertyEquals = new Dictionary<string, string> { ["kind"] = "production" },
                PropertyContains = new Dictionary<string, string> { ["name"] = "QUERY" },
                KeywordText = "  graph ",
                KeywordCategory = "  Concept "
            },
            new GraphSort("name", GraphSortDirection.Ascending),
            skip: 3,
            limit: 25);

        query.Cypher.Should().Contain("ANY(label IN labels(n) WHERE label IN $labels)")
            .And.Contain("coalesce(n.id, elementId(n)) IN $nodeIds")
            .And.Contain("propertyEquals0")
            .And.Contain("propertyContains1")
            .And.Contain("$keywordText")
            .And.Contain("$keywordCategory")
            .And.Contain("ORDER BY coalesce(n.name, n.source, n.value, '') ASC");
        query.Parameters["skip"].Should().Be(3);
        query.Parameters["limit"].Should().Be(25);
        query.Parameters["projectContextNormalized"].Should().Be("codemeridian");
        query.Parameters["propertyContains1"].Should().Be("query");
        query.Parameters["keywordText"].Should().Be("graph");
        query.Parameters["keywordCategory"].Should().Be("concept");
    }

    [Fact]
    public void BuildNodeQuery_UsesFallbackOrderWhenNoFiltersOrSortAreProvided()
    {
        var query = Neo4jGraphQueryBuilder.BuildNodeQuery(new GraphNodeFilter(), null, 0, 10);

        query.Cypher.Should().NotContain("WHERE")
            .And.Contain("ORDER BY coalesce(n.id, elementId(n))")
            .And.Contain("SKIP $skip")
            .And.Contain("LIMIT $limit");
        query.Parameters.Should().ContainKeys("skip", "limit");
    }

    [Fact]
    public void BuildRelationshipQuery_IncludesAllConfiguredFiltersAndDescendingSort()
    {
        var query = Neo4jGraphQueryBuilder.BuildRelationshipQuery(
            new GraphRelationshipFilter
            {
                RelationshipTypes = ["Calls"],
                FromNodeIds = ["from"],
                ToNodeIds = ["to"],
                ProjectContext = "Project",
                PropertyEquals = new Dictionary<string, string> { ["kind"] = "call" },
                PropertyContains = new Dictionary<string, string> { ["label"] = "ASYNC" }
            },
            new GraphSort("type", GraphSortDirection.Descending),
            skip: 2,
            limit: 8);

        query.Cypher.Should().Contain("type(r) IN $relationshipTypes")
            .And.Contain("$fromNodeIds")
            .And.Contain("$toNodeIds")
            .And.Contain("projectContextNormalized")
            .And.Contain("ORDER BY type(r) DESC");
        query.Parameters["propertyEquals0"].Should().Be("call");
        query.Parameters["propertyContains1"].Should().Be("async");
    }

    [Fact]
    public void BuildQueries_RejectUnsafePropertyNames()
    {
        var act = () => Neo4jGraphQueryBuilder.BuildNodeQuery(
            new GraphNodeFilter { PropertyEquals = new Dictionary<string, string> { ["name; DROP"] = "value" } },
            null,
            0,
            1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void BuildQueries_RejectUnsupportedSortFields()
    {
        var act = () => Neo4jGraphQueryBuilder.BuildRelationshipQuery(
            new GraphRelationshipFilter(),
            new GraphSort("unsupported", GraphSortDirection.Ascending),
            0,
            1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
