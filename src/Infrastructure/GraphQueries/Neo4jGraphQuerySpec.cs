namespace CodeMeridian.Infrastructure.GraphQueries;

internal sealed record Neo4jGraphQuerySpec(
    string Cypher,
    IReadOnlyDictionary<string, object?> Parameters);
