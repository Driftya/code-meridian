using CodeMeridian.Core.CodeGraph;
using Neo4j.Driver;

namespace CodeMeridian.Infrastructure.Graph;

/// <summary>
/// Structural analytics queries — impact, hotspots, coverage, churn, etc.
/// Core CRUD and schema live in Neo4jCodeGraphRepository.cs.
/// GDS (PageRank, Betweenness, Louvain) live in Neo4jCodeGraphRepository.Gds.cs.
/// </summary>
public sealed partial class Neo4jCodeGraphRepository
{
    public async Task<IReadOnlyList<(CodeNode Source, CodeNode Target, string RelationshipType)>> FindCrossProjectDependenciesAsync(
        string? projectContext = null,
        CancellationToken cancellationToken = default)
    {
        await using var session = _driver.AsyncSession();

        const string cypher = """
            MATCH (source:CodeNode)-[r:Calls|Uses|DependsOn]->(target:CodeNode)
            WHERE source.projectContext <> target.projectContext
              AND ($projectContext IS NULL
                   OR source.projectContext = $projectContext
                   OR target.projectContext = $projectContext)
            RETURN source, target, type(r) AS relType
            ORDER BY source.projectContext, target.projectContext
            LIMIT 100
            """;

        var cursor = await session.RunAsync(cypher, new { projectContext = (object?)projectContext });
        var results = new List<(CodeNode, CodeNode, string)>();

        await foreach (var record in cursor.WithCancellation(cancellationToken))
        {
            results.Add((
                MapToCodeNode(record["source"].As<INode>()),
                MapToCodeNode(record["target"].As<INode>()),
                record["relType"].As<string>()));
        }

        return results;
    }

    public async Task<IReadOnlyList<CodeNode>> FindCoverageGapsAsync(
        string? projectContext = null,
        CancellationToken cancellationToken = default)
    {
        await using var session = _driver.AsyncSession();

        // Test namespaces are identified heuristically by name containing "Test"
        const string cypher = """
            MATCH (prod:CodeNode)
            WHERE prod.type IN ['Class', 'Method']
              AND ($projectContext IS NULL OR prod.projectContext = $projectContext)
              AND NOT prod.namespace CONTAINS 'Test'
              AND NOT EXISTS {
                MATCH (test:CodeNode)-[:Calls]->(prod)
                WHERE test.namespace CONTAINS 'Test'
              }
            RETURN prod
            ORDER BY prod.type, prod.name
            LIMIT 100
            """;

        var cursor = await session.RunAsync(cypher, new { projectContext = (object?)projectContext });
        var results = new List<CodeNode>();

        await foreach (var record in cursor.WithCancellation(cancellationToken))
            results.Add(MapToCodeNode(record["prod"].As<INode>()));

        return results;
    }

    public async Task<IReadOnlyList<(CodeNode Node, DateTimeOffset ChangedAt, string ChangeType)>> FindRecentlyChangedAsync(
        string? projectContext,
        TimeSpan window,
        CancellationToken cancellationToken = default)
    {
        await using var session = _driver.AsyncSession();

        var cutoffMs = DateTimeOffset.UtcNow.Subtract(window).ToUnixTimeMilliseconds();

        const string cypher = """
            MATCH (n:CodeNode)
            WHERE ($projectContext IS NULL OR n.projectContext = $projectContext)
              AND (n.createdAt >= $cutoff OR n.updatedAt >= $cutoff)
            WITH n,
                 CASE WHEN n.createdAt >= $cutoff AND (n.updatedAt IS NULL OR n.createdAt = n.updatedAt)
                      THEN 'created' ELSE 'updated' END AS changeType,
                 CASE WHEN n.updatedAt IS NOT NULL THEN n.updatedAt ELSE n.createdAt END AS changedAt
            RETURN n, changedAt, changeType
            ORDER BY changedAt DESC
            LIMIT 50
            """;

        var cursor = await session.RunAsync(cypher, new
        {
            projectContext = (object?)projectContext,
            cutoff = cutoffMs
        });

        var results = new List<(CodeNode, DateTimeOffset, string)>();

        await foreach (var record in cursor.WithCancellation(cancellationToken))
        {
            var changedAt = DateTimeOffset.FromUnixTimeMilliseconds(record["changedAt"].As<long>());
            results.Add((MapToCodeNode(record["n"].As<INode>()), changedAt, record["changeType"].As<string>()));
        }

        return results;
    }

    public async Task<IReadOnlyList<(CodeNode Node, int Distance)>> FindImpactAsync(
        string nodeId,
        int depth = 5,
        CancellationToken cancellationToken = default)
    {
        await using var session = _driver.AsyncSession();

        var cypher = $$"""
            MATCH path = (caller:CodeNode)-[:Calls|Uses|DependsOn*1..{{depth}}]->(target:CodeNode {id: $nodeId})
            WITH caller, min(length(path)) AS dist
            RETURN caller, dist
            ORDER BY dist ASC
            LIMIT 50
            """;

        var cursor = await session.RunAsync(cypher, new { nodeId });
        var results = new List<(CodeNode, int)>();

        await foreach (var record in cursor.WithCancellation(cancellationToken))
            results.Add((MapToCodeNode(record["caller"].As<INode>()), record["dist"].As<int>()));

        return results;
    }

    public async Task<IReadOnlyList<(CodeNode Node, int Distance)>> FindDownstreamAsync(
        string nodeId,
        int depth = 5,
        CancellationToken cancellationToken = default)
    {
        await using var session = _driver.AsyncSession();

        var cypher = $$"""
            MATCH path = (source:CodeNode {id: $nodeId})-[:Calls|Uses|DependsOn*1..{{depth}}]->(downstream:CodeNode)
            WITH downstream, min(length(path)) AS dist
            RETURN downstream, dist
            ORDER BY dist ASC
            LIMIT 50
            """;

        var cursor = await session.RunAsync(cypher, new { nodeId });
        var results = new List<(CodeNode, int)>();

        await foreach (var record in cursor.WithCancellation(cancellationToken))
            results.Add((MapToCodeNode(record["downstream"].As<INode>()), record["dist"].As<int>()));

        return results;
    }

    public async Task<IReadOnlyList<(CodeNode Node, int FanIn)>> FindHotspotsAsync(
        string? projectContext,
        int limit = 15,
        CancellationToken cancellationToken = default)
    {
        await using var session = _driver.AsyncSession();

        const string cypher = """
            MATCH (caller:CodeNode)-[:Calls|Uses|DependsOn]->(n:CodeNode)
            WHERE ($projectContext IS NULL OR n.projectContext = $projectContext)
              AND n.type IN ['Method', 'Class', 'Interface']
            RETURN n, count(caller) AS fanIn
            ORDER BY fanIn DESC
            LIMIT $limit
            """;

        var cursor = await session.RunAsync(cypher, new
        {
            projectContext = (object?)projectContext,
            limit
        });

        var results = new List<(CodeNode, int)>();

        await foreach (var record in cursor.WithCancellation(cancellationToken))
            results.Add((MapToCodeNode(record["n"].As<INode>()), record["fanIn"].As<int>()));

        return results;
    }

    public async Task<IReadOnlyList<(CodeNode Node, string? ViaRelationship)>> FindConnectionAsync(
        string fromId,
        string toId,
        CancellationToken cancellationToken = default)
    {
        await using var session = _driver.AsyncSession();

        const string cypher = """
            MATCH path = shortestPath(
              (a:CodeNode {id: $fromId})-[*..10]-(b:CodeNode {id: $toId})
            )
            RETURN [n IN nodes(path) | n] AS pathNodes,
                   [r IN relationships(path) | type(r)] AS relTypes
            """;

        var cursor = await session.RunAsync(cypher, new { fromId, toId });
        var records = await cursor.ToListAsync();

        if (records.Count == 0) return [];

        var record = records[0];
        var pathNodes = record["pathNodes"].As<List<INode>>();
        var relTypes = record["relTypes"].As<List<string>>();

        var result = new List<(CodeNode, string?)>();
        for (var i = 0; i < pathNodes.Count; i++)
        {
            var via = i < relTypes.Count ? relTypes[i] : null;
            result.Add((MapToCodeNode(pathNodes[i]), via));
        }

        return result;
    }

    public async Task<IReadOnlyList<CodeNode>> FindUnreferencedAsync(
        string? projectContext,
        CancellationToken cancellationToken = default)
    {
        await using var session = _driver.AsyncSession();

        const string cypher = """
            MATCH (n:CodeNode)
            WHERE n.type IN ['Method', 'Class']
              AND ($projectContext IS NULL OR n.projectContext = $projectContext)
              AND NOT ()-[:Calls|Uses|Contains]->(n)
            RETURN n
            ORDER BY n.type, n.name
            LIMIT 100
            """;

        var cursor = await session.RunAsync(cypher, new { projectContext = (object?)projectContext });
        var results = new List<CodeNode>();

        await foreach (var record in cursor.WithCancellation(cancellationToken))
            results.Add(MapToCodeNode(record["n"].As<INode>()));

        return results;
    }

    public async Task<IReadOnlyList<CodeNode>> FindLargeNodesAsync(
        string? projectContext = null,
        int classThreshold = 400,
        int methodThreshold = 40,
        CancellationToken cancellationToken = default)
    {
        await using var session = _driver.AsyncSession();

        const string cypher = """
            MATCH (n:CodeNode)
            WHERE n.lineCount IS NOT NULL
              AND ($projectContext IS NULL OR n.projectContext = $projectContext)
              AND (
                (n.type = 'Class' AND n.lineCount > $classThreshold) OR
                (n.type = 'Method' AND n.lineCount > $methodThreshold)
              )
              AND NOT (
                coalesce(n.namespace, '') CONTAINS 'Test' OR
                coalesce(n.filePath, '') CONTAINS 'Test' OR
                coalesce(n.filePath, '') CONTAINS 'test' OR
                coalesce(n.filePath, '') CONTAINS '.spec.'
              )
            RETURN n
            ORDER BY n.lineCount DESC
            LIMIT 50
            """;

        var cursor = await session.RunAsync(cypher, new
        {
            projectContext = (object?)projectContext,
            classThreshold,
            methodThreshold
        });

        var results = new List<CodeNode>();
        await foreach (var record in cursor.WithCancellation(cancellationToken))
            results.Add(MapToCodeNode(record["n"].As<INode>()));

        return results;
    }

    public async Task<EditingContext> GetContextForEditingAsync(
        string nodeId,
        CancellationToken cancellationToken = default)
    {
        await using var session = _driver.AsyncSession();

        const string cypher = """
            MATCH (n:CodeNode {id: $nodeId})
            OPTIONAL MATCH (caller:CodeNode)-[:Calls|Uses]->(n)
            WITH n, collect(DISTINCT caller)[..10] AS callers
            OPTIONAL MATCH (n)-[:Calls|Uses]->(callee:CodeNode)
            WITH n, callers, collect(DISTINCT callee)[..10] AS callees
            OPTIONAL MATCH (n)-[:Implements]->(iface:CodeNode)
            WITH n, callers, callees, collect(DISTINCT iface)[..10] AS interfaces
            RETURN n, callers, callees, interfaces
            """;

        var cursor = await session.RunAsync(cypher, new { nodeId });
        var records = await cursor.ToListAsync();

        if (records.Count == 0)
            return new EditingContext(null, [], [], []);

        var record = records[0];
        var node = record["n"].As<INode?>();
        var callers = record["callers"].As<List<INode>>().Select(MapToCodeNode).ToList();
        var callees = record["callees"].As<List<INode>>().Select(MapToCodeNode).ToList();
        var interfaces = record["interfaces"].As<List<INode>>().Select(MapToCodeNode).ToList();

        return new EditingContext(
            node is null ? null : MapToCodeNode(node),
            callers,
            callees,
            interfaces);
    }

    public async Task<IReadOnlyList<(CodeNode Node, int LineCount, int FanIn)>> FindGodClassesAsync(
        string? projectContext = null,
        int lineThreshold = 300,
        int fanInThreshold = 3,
        CancellationToken cancellationToken = default)
    {
        await using var session = _driver.AsyncSession();

        const string cypher = """
            MATCH (n:CodeNode)
            WHERE n.type = 'Class'
              AND n.lineCount IS NOT NULL
              AND ($projectContext IS NULL OR n.projectContext = $projectContext)
              AND n.lineCount > $lineThreshold
              AND NOT (
                coalesce(n.namespace, '') CONTAINS 'Test' OR
                coalesce(n.filePath, '') CONTAINS 'Test' OR
                coalesce(n.filePath, '') CONTAINS 'test' OR
                coalesce(n.filePath, '') CONTAINS '.spec.'
              )
            OPTIONAL MATCH (caller:CodeNode)-[:Calls|Uses|Implements|Inherits]->(n)
            WITH n, n.lineCount AS lineCount, count(DISTINCT caller) AS fanIn
            WHERE fanIn > $fanInThreshold
            RETURN n, lineCount, fanIn
            ORDER BY (fanIn * 10 + lineCount) DESC
            LIMIT 20
            """;

        var cursor = await session.RunAsync(cypher, new
        {
            projectContext = (object?)projectContext,
            lineThreshold,
            fanInThreshold
        });

        var results = new List<(CodeNode, int, int)>();
        await foreach (var record in cursor.WithCancellation(cancellationToken))
        {
            results.Add((
                MapToCodeNode(record["n"].As<INode>()),
                record["lineCount"].As<int>(),
                record["fanIn"].As<int>()));
        }

        return results;
    }

    public async Task<IReadOnlyList<(string FromNamespace, string ToNamespace)>> FindCyclesAsync(
        string? projectContext = null,
        CancellationToken cancellationToken = default)
    {
        await using var session = _driver.AsyncSession();

        // Detect namespace-level bidirectional dependencies (A→B and B→A)
        const string cypher = """
            MATCH (a:CodeNode)-[:Calls|Uses|DependsOn]->(b:CodeNode)
            WHERE a.namespace IS NOT NULL
              AND b.namespace IS NOT NULL
              AND a.namespace <> b.namespace
              AND ($projectContext IS NULL OR a.projectContext = $projectContext)
            WITH DISTINCT a.namespace AS nsA, b.namespace AS nsB
            MATCH (c:CodeNode)-[:Calls|Uses|DependsOn]->(d:CodeNode)
            WHERE c.namespace = nsB AND d.namespace = nsA
              AND c.namespace <> d.namespace
            RETURN DISTINCT nsA AS fromNamespace, nsB AS toNamespace
            ORDER BY fromNamespace, toNamespace
            LIMIT 50
            """;

        var cursor = await session.RunAsync(cypher, new { projectContext = (object?)projectContext });
        var results = new List<(string, string)>();

        await foreach (var record in cursor.WithCancellation(cancellationToken))
            results.Add((record["fromNamespace"].As<string>(), record["toNamespace"].As<string>()));

        return results;
    }

    public async Task<IReadOnlyList<(CodeNode Source, CodeNode Target, string Violation)>> FindArchitectureViolationsAsync(
        string? projectContext = null,
        CancellationToken cancellationToken = default)
    {
        await using var session = _driver.AsyncSession();

        // Clean Architecture rules: Core must not depend on Application/Infrastructure/McpServer;
        // Application must not depend on Infrastructure/McpServer.
        const string cypher = """
            MATCH (source:CodeNode)-[:Calls|Uses|DependsOn]->(target:CodeNode)
            WHERE ($projectContext IS NULL OR source.projectContext = $projectContext)
              AND source.namespace IS NOT NULL
              AND target.namespace IS NOT NULL
              AND (
                (
                  (source.namespace CONTAINS 'Core')
                  AND (target.namespace CONTAINS 'Infrastructure'
                       OR target.namespace CONTAINS 'McpServer'
                       OR target.namespace CONTAINS 'Application')
                )
                OR
                (
                  (source.namespace CONTAINS 'Application')
                  AND (target.namespace CONTAINS 'Infrastructure'
                       OR target.namespace CONTAINS 'McpServer')
                )
              )
            WITH source, target,
                 CASE
                   WHEN source.namespace CONTAINS 'Core' THEN 'Core \u2192 ' + target.namespace
                   WHEN source.namespace CONTAINS 'Application' THEN 'Application \u2192 ' + target.namespace
                   ELSE source.namespace + ' \u2192 ' + target.namespace
                 END AS violation
            RETURN source, target, violation
            ORDER BY violation
            LIMIT 50
            """;

        var cursor = await session.RunAsync(cypher, new { projectContext = (object?)projectContext });
        var results = new List<(CodeNode, CodeNode, string)>();

        await foreach (var record in cursor.WithCancellation(cancellationToken))
        {
            results.Add((
                MapToCodeNode(record["source"].As<INode>()),
                MapToCodeNode(record["target"].As<INode>()),
                record["violation"].As<string>()));
        }

        return results;
    }

    public async Task<IReadOnlyList<(CodeNode Node, int ChangeCount)>> FindHighChurnAsync(
        string? projectContext = null,
        int threshold = 3,
        CancellationToken cancellationToken = default)
    {
        await using var session = _driver.AsyncSession();

        const string cypher = """
            MATCH (n:CodeNode)
            WHERE n.changeCount IS NOT NULL
              AND n.changeCount >= $threshold
              AND ($projectContext IS NULL OR n.projectContext = $projectContext)
            RETURN n, n.changeCount AS changeCount
            ORDER BY changeCount DESC
            LIMIT 50
            """;

        var cursor = await session.RunAsync(cypher, new
        {
            projectContext = (object?)projectContext,
            threshold
        });

        var results = new List<(CodeNode, int)>();

        await foreach (var record in cursor.WithCancellation(cancellationToken))
            results.Add((MapToCodeNode(record["n"].As<INode>()), record["changeCount"].As<int>()));

        return results;
    }
}
