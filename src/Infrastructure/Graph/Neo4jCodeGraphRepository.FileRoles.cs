namespace CodeMeridian.Infrastructure.Graph;

public sealed partial class Neo4jCodeGraphRepository
{
    private static string FileRoleExpression(string alias) =>
        $"""
        CASE
          WHEN {alias}.fileRole IS NOT NULL THEN {alias}.fileRole
          WHEN coalesce({alias}.filePathNormalized CONTAINS '/bin/', false)
            OR coalesce({alias}.filePathNormalized CONTAINS '/obj/', false)
            OR coalesce({alias}.filePathNormalized CONTAINS '/node_modules/', false)
            OR coalesce({alias}.filePathNormalized CONTAINS '/dist/', false)
            OR coalesce({alias}.filePathNormalized CONTAINS '/build/', false)
            OR coalesce({alias}.filePathNormalized CONTAINS '/coverage/', false)
            THEN 'BuildArtifact'
          WHEN coalesce({alias}.filePathNormalized ENDS WITH 'modelsnapshot.cs', false) THEN 'Snapshot'
          WHEN coalesce({alias}.filePathNormalized CONTAINS '/migrations/', false)
            AND coalesce({alias}.filePathNormalized ENDS WITH '.cs', false) THEN 'Migration'
          WHEN coalesce({alias}.filePathNormalized CONTAINS '.generated.', false)
            OR coalesce({alias}.filePathNormalized ENDS WITH '.g.cs', false)
            OR coalesce({alias}.filePathNormalized ENDS WITH '.designer.cs', false)
            OR coalesce({alias}.filePathNormalized ENDS WITH '/openapi.generated.ts', false)
            OR coalesce({alias}.filePathNormalized ENDS WITH '/graphql.generated.ts', false)
            THEN 'Generated'
          WHEN coalesce({alias}.filePathNormalized CONTAINS '/tests/', false)
            OR coalesce({alias}.filePathNormalized CONTAINS '/test/', false)
            OR coalesce({alias}.filePathNormalized CONTAINS 'test', false)
            OR coalesce({alias}.filePathNormalized CONTAINS '/__tests__/', false)
            OR coalesce({alias}.filePathNormalized CONTAINS '.test.', false)
            OR coalesce({alias}.filePathNormalized CONTAINS '.spec.', false)
            OR coalesce({alias}.nameNormalized CONTAINS 'test', false)
            OR coalesce({alias}.namespaceNormalized CONTAINS 'test', false)
            THEN 'Test'
          WHEN coalesce({alias}.filePathNormalized ENDS WITH '.md', false)
            OR coalesce({alias}.filePathNormalized ENDS WITH '.mdx', false)
            OR coalesce({alias}.filePathNormalized ENDS WITH '.txt', false)
            THEN 'Documentation'
          WHEN coalesce({alias}.filePathNormalized ENDS WITH '/.env', false)
            OR coalesce({alias}.filePathNormalized ENDS WITH '/appsettings.json', false)
            OR coalesce({alias}.filePathNormalized CONTAINS '/appsettings.', false)
            OR coalesce({alias}.filePathNormalized ENDS WITH '/meridian.json', false)
            OR coalesce({alias}.filePathNormalized ENDS WITH '/meridian.sample.json', false)
            OR coalesce({alias}.filePathNormalized ENDS WITH '/docker-compose.yml', false)
            OR coalesce({alias}.filePathNormalized ENDS WITH '/docker-compose.yaml', false)
            OR coalesce({alias}.filePathNormalized CONTAINS '/docker-compose', false)
            THEN 'Configuration'
          WHEN {alias}.filePathNormalized IS NULL THEN 'Unknown'
          ELSE 'Source'
        END
        """;
}
