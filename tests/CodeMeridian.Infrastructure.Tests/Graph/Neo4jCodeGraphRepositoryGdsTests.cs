using CodeMeridian.Infrastructure.Graph;
using FluentAssertions;

namespace CodeMeridian.Infrastructure.Tests.Graph;

public sealed class Neo4jCodeGraphRepositoryGdsTests
{
    [Fact]
    public void BuildRelationshipProjection_FiltersRelationshipTypesMissingFromSparseGraph()
    {
        var projection = Neo4jCodeGraphRepository.BuildRelationshipProjection(
            ["Calls", "Uses", "UsesClass", "DependsOn", "Contains"],
            ["Contains", "Calls", "DependsOn"],
            undirected: true);

        projection.Should().Be(
            "{ Calls: {orientation: 'UNDIRECTED'}, DependsOn: {orientation: 'UNDIRECTED'}, Contains: {orientation: 'UNDIRECTED'} }");
    }

    [Fact]
    public void BuildRelationshipProjection_WhenNoPreferredTypesExist_ReturnsNull()
    {
        var projection = Neo4jCodeGraphRepository.BuildRelationshipProjection(
            ["Calls", "Uses"],
            ["Contains"],
            undirected: true);

        projection.Should().BeNull();
    }
}
