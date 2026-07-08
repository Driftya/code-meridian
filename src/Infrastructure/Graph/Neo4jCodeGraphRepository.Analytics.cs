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
    private const string StructuralDependencyRelationships = "Calls|Uses|DependsOn|UsesClass|UsesId|DefinesSelector|ImportsStyle|UsesCssVariable|DefinesCssVariable|Reads|Writes|PublishesTo|SubscribesTo";
    private const string StructuralTraversalRelationships = StructuralDependencyRelationships + "|Implements|Inherits";
    private const string ConnectionRelationships = StructuralTraversalRelationships + "|Contains";
    private const string WorkflowAdjacentTypeList = "['ApiEndpoint','File','ExternalConcept','MessageTopic','ExternalService','Diagnostic','ConfigurationFile','ConfigurationKey','ConfigurationEntry']";

    public async Task<IReadOnlyList<(CodeNode Source, CodeNode Target, string RelationshipType)>> FindCrossProjectDependenciesAsync(
        string? projectContext = null,
        CancellationToken cancellationToken = default)
    {
        await using var session = _driver.AsyncSession();

        var cypher = $"""
            MATCH (source:CodeNode)-[r:{StructuralDependencyRelationships}]->(target:CodeNode)
            WHERE source.projectContextNormalized <> target.projectContextNormalized
              AND ($projectContextNormalized IS NULL
                   OR source.projectContextNormalized = $projectContextNormalized
                   OR target.projectContextNormalized = $projectContextNormalized)
            RETURN source, target, type(r) AS relType
            ORDER BY source.projectContext, target.projectContext
            LIMIT 100
            """;

        var cursor = await session.RunAsync(cypher, new { projectContextNormalized = (object?)Normalize(projectContext) });
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

        var prodRole = FileRoleExpression("prod");
        var testRole = FileRoleExpression("test");
        var testPredicate = $"""
            (
                {testRole} = 'Test'
                OR test.namespaceNormalized CONTAINS 'test'
                OR test.filePathNormalized CONTAINS 'test'
                OR test.nameNormalized CONTAINS 'test'
                OR test.filePathNormalized CONTAINS '.spec.'
                OR test.filePathNormalized CONTAINS '.test.'
            )
            """;
        var cypher = $@"
            MATCH (prod:CodeNode)
            WHERE prod.type IN ['Class', 'Method']
              AND ($projectContextNormalized IS NULL OR prod.projectContextNormalized = $projectContextNormalized)
              AND {prodRole} IN ['Source', 'Unknown']
              AND NOT EXISTS {{
                MATCH (test:CodeNode)-[:Calls]->(prod)
                WHERE {testPredicate}
              }}
            OPTIONAL MATCH (caller:CodeNode)-[:Calls|Uses|DependsOn|UsesClass|UsesId|DefinesSelector|ImportsStyle|UsesCssVariable|DefinesCssVariable]->(prod)
            WITH prod, count(DISTINCT caller) AS fanIn,
                 CASE
                   WHEN prod.filePathNormalized CONTAINS '/program.cs' THEN 1
                   WHEN prod.nameNormalized IN ['dependencyinjection', 'neo4joptions', 'embeddingoptions'] THEN 1
                   WHEN prod.nameNormalized ENDS WITH 'options' THEN 1
                   WHEN prod.nameNormalized ENDS WITH 'endpoints' THEN 1
                   WHEN prod.nameNormalized ENDS WITH 'tools' THEN 1
                   ELSE 0
                 END AS infrastructureRank
            RETURN prod
            ORDER BY infrastructureRank ASC,
                     fanIn DESC,
                     coalesce(prod.lineCount, 0) DESC,
                     coalesce(prod.changeCount, 0) DESC,
                     prod.type,
                     prod.name
            LIMIT 100
            ";

        var cursor = await session.RunAsync(cypher, new { projectContextNormalized = (object?)Normalize(projectContext) });
        var results = new List<CodeNode>();

        await foreach (var record in cursor.WithCancellation(cancellationToken))
            results.Add(MapToCodeNode(record["prod"].As<INode>()));

        return results;
    }

    public async Task<IReadOnlyList<(CodeNode Node, string MatchType)>> FindRelatedTestsAsync(
        string nodeId,
        string? projectContext = null,
        CancellationToken cancellationToken = default)
    {
        await using var session = _driver.AsyncSession();

        var testRole = FileRoleExpression("test");
        var testPredicate = $"""
            (
                {testRole} = 'Test'
                OR test.namespaceNormalized CONTAINS 'test'
                OR test.filePathNormalized CONTAINS 'test'
                OR test.nameNormalized CONTAINS 'test'
                OR test.filePathNormalized CONTAINS '.spec.'
                OR test.filePathNormalized CONTAINS '.test.'
            )
            """;
        const string directCypherPrefix = """
            MATCH (test:CodeNode)-[:Calls]->(target:CodeNode {id: $nodeId})
            WHERE ($projectContextNormalized IS NULL OR test.projectContextNormalized = $projectContextNormalized)
            """;
        var directCypher = $@"
            {directCypherPrefix}
              AND {testPredicate}
            RETURN test AS node, 'direct' AS matchType
            ORDER BY test.name
            LIMIT 25
            ";

        const string heuristicCypherPrefix = """
            MATCH (target:CodeNode {id: $nodeId})
            MATCH (test:CodeNode)
            WHERE ($projectContextNormalized IS NULL OR test.projectContextNormalized = $projectContextNormalized)
            """;
        var heuristicCypher = $@"
            {heuristicCypherPrefix}
              AND {testPredicate}
              AND (
                test.filePath = target.filePath
                OR test.namespace = target.namespace
                OR test.filePathNormalized CONTAINS target.nameNormalized
                OR test.nameNormalized CONTAINS target.nameNormalized
                OR target.nameNormalized CONTAINS test.nameNormalized
              )
              AND NOT EXISTS {{
                MATCH (test)-[:Calls]->(target)
              }}
            RETURN test AS node, 'heuristic' AS matchType
            ORDER BY test.name
            LIMIT 25
            ";

        var matches = new List<(CodeNode Node, string MatchType)>();

        foreach (var cypher in new[] { directCypher, heuristicCypher })
        {
            var cursor = await session.RunAsync(cypher, new
            {
                nodeId,
                projectContextNormalized = (object?)Normalize(projectContext)
            });

            await foreach (var record in cursor.WithCancellation(cancellationToken))
            {
                matches.Add((
                    MapToCodeNode(record["node"].As<INode>()),
                    record["matchType"].As<string>()));
            }
        }

        return matches
            .GroupBy(match => match.Node.Id)
            .Select(group => group.First())
            .ToArray();
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
            WHERE ($projectContextNormalized IS NULL OR n.projectContextNormalized = $projectContextNormalized)
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
            projectContextNormalized = (object?)Normalize(projectContext),
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
        var paths = await FindImpactPathsAsync(nodeId, depth, cancellationToken);
        return paths
            .Select(path => (path.Node, path.Distance))
            .ToList();
    }

    public async Task<IReadOnlyList<ImpactPath>> FindImpactPathsAsync(
        string nodeId,
        int depth = 5,
        CancellationToken cancellationToken = default)
    {
        await using var session = _driver.AsyncSession();

        var maxDepth = Math.Clamp(depth, 1, 8);
        var cypher = $$"""
            MATCH (target:CodeNode {id: $nodeId})
            WITH target, target.type AS targetType
            CALL {
                WITH target
                OPTIONAL MATCH (target)-[:Contains*0..2]->(targetMember:CodeNode)
                WITH target, collect(DISTINCT target) + collect(DISTINCT targetMember) AS targets
                UNWIND targets AS targetNode
                WITH DISTINCT target, targetNode, targets
                WHERE targetNode IS NOT NULL
                MATCH path = (caller:CodeNode)-[:{{StructuralTraversalRelationships}}*1..8]->(targetNode)
                WHERE none(candidate IN targets WHERE candidate = caller)
                WITH target,
                     targetNode,
                     caller,
                     path,
                     length(path) AS rawDist,
                     [n IN nodes(path) | n] AS pathNodes,
                     [r IN relationships(path) | { type: type(r), confidence: r.confidence }] AS pathRelationships
                ORDER BY caller.id, rawDist ASC
                WITH target,
                     targetNode,
                     caller,
                     collect({
                         rawDist: rawDist,
                         pathNodes: pathNodes,
                         pathRelationships: pathRelationships
                     })[0] AS best
                WITH target,
                     targetNode,
                     caller,
                     best.rawDist AS rawDist,
                     best.pathNodes AS pathNodes,
                     best.pathRelationships AS pathRelationships
                RETURN caller,
                       rawDist + CASE WHEN targetNode = target THEN 0 ELSE 1 END AS dist,
                       CASE
                           WHEN targetNode <> target AND caller.type IN {{WorkflowAdjacentTypeList}} THEN 'workflow'
                           WHEN targetNode <> target AND last(pathRelationships).type = 'DependsOn' THEN 'dependency'
                           WHEN targetNode <> target THEN 'member'
                           WHEN caller.type IN {{WorkflowAdjacentTypeList}} THEN 'workflow'
                           WHEN last(pathRelationships).type IN ['DependsOn', 'Implements', 'Inherits'] THEN 'dependency'
                           ELSE 'direct-class'
                       END AS bucket,
                       CASE
                           WHEN targetNode <> target AND caller.type IN {{WorkflowAdjacentTypeList}} THEN 4
                           WHEN targetNode <> target AND last(pathRelationships).type = 'DependsOn' THEN 3
                           WHEN targetNode <> target THEN 2
                           WHEN caller.type IN {{WorkflowAdjacentTypeList}} THEN 4
                           WHEN last(pathRelationships).type IN ['DependsOn', 'Implements', 'Inherits'] THEN 3
                           ELSE 1
                       END AS bucketRank,
                       CASE
                           WHEN targetNode = target THEN pathNodes
                           ELSE pathNodes + [target]
                       END AS pathNodes,
                       CASE
                           WHEN targetNode = target THEN pathRelationships
                           ELSE pathRelationships + [{ type: 'Contains', confidence: 1.0 }]
                       END AS pathRelationships

                UNION

                WITH target, targetType
                WITH target, targetType
                WHERE targetType = 'Interface'
                MATCH (implementer:CodeNode)-[implementationRel:Implements|Inherits]->(target)
                MATCH path = (caller:CodeNode)-[:{{StructuralTraversalRelationships}}*1..8]->(implementer)
                WHERE caller <> target AND caller <> implementer
                WITH target,
                     implementationRel,
                     caller,
                     path,
                     length(path) AS rawDist,
                     [n IN nodes(path) | n] AS pathNodes,
                     [r IN relationships(path) | { type: type(r), confidence: r.confidence }] AS pathRelationships
                ORDER BY caller.id, rawDist ASC
                WITH target,
                     implementationRel,
                     caller,
                     collect({
                         rawDist: rawDist,
                         pathNodes: pathNodes,
                         pathRelationships: pathRelationships
                     })[0] AS best
                RETURN caller,
                       best.rawDist + 1 AS dist,
                       CASE
                           WHEN caller.type IN {{WorkflowAdjacentTypeList}} THEN 'workflow'
                           ELSE 'dependency'
                       END AS bucket,
                       CASE
                           WHEN caller.type IN {{WorkflowAdjacentTypeList}} THEN 4
                           ELSE 3
                       END AS bucketRank,
                       best.pathNodes + [target] AS pathNodes,
                       best.pathRelationships + [{ type: type(implementationRel), confidence: implementationRel.confidence }] AS pathRelationships

                UNION

                WITH target, targetType
                WITH target, targetType
                WHERE targetType = 'Interface'
                MATCH (implementer:CodeNode)-[implementationRel:Implements|Inherits]->(target)
                MATCH (implementer)-[:Contains*1..2]->(implementerMember:CodeNode)
                MATCH path = (caller:CodeNode)-[:{{StructuralTraversalRelationships}}*1..8]->(implementerMember)
                WHERE caller <> target
                  AND caller <> implementer
                  AND caller <> implementerMember
                WITH target,
                     implementer,
                     implementationRel,
                     caller,
                     path,
                     length(path) AS rawDist,
                     [n IN nodes(path) | n] AS pathNodes,
                     [r IN relationships(path) | { type: type(r), confidence: r.confidence }] AS pathRelationships
                ORDER BY caller.id, rawDist ASC
                WITH target,
                     implementer,
                     implementationRel,
                     caller,
                     collect({
                         rawDist: rawDist,
                         pathNodes: pathNodes,
                         pathRelationships: pathRelationships
                     })[0] AS best
                RETURN caller,
                       best.rawDist + 2 AS dist,
                       CASE
                           WHEN caller.type IN {{WorkflowAdjacentTypeList}} THEN 'workflow'
                           WHEN last(best.pathRelationships).type = 'DependsOn' THEN 'dependency'
                           ELSE 'member'
                       END AS bucket,
                       CASE
                           WHEN caller.type IN {{WorkflowAdjacentTypeList}} THEN 4
                           WHEN last(best.pathRelationships).type = 'DependsOn' THEN 3
                           ELSE 2
                       END AS bucketRank,
                       best.pathNodes + [implementer, target] AS pathNodes,
                       best.pathRelationships + [{ type: 'Contains', confidence: 1.0 }, { type: type(implementationRel), confidence: implementationRel.confidence }] AS pathRelationships

                UNION

                WITH target, targetType
                WITH target, targetType
                WHERE targetType = 'Class'
                MATCH (target)-[implementationRel:Implements|Inherits]->(abstraction:CodeNode)
                MATCH path = (caller:CodeNode)-[:{{StructuralTraversalRelationships}}*1..8]->(abstraction)
                WHERE caller <> target
                  AND caller <> abstraction
                WITH target,
                     implementationRel,
                     caller,
                     path,
                     length(path) AS rawDist,
                     [n IN nodes(path) | n] AS pathNodes,
                     [r IN relationships(path) | { type: type(r), confidence: r.confidence }] AS pathRelationships
                ORDER BY caller.id, rawDist ASC
                WITH target,
                     implementationRel,
                     caller,
                     collect({
                         rawDist: rawDist,
                         pathNodes: pathNodes,
                         pathRelationships: pathRelationships
                     })[0] AS best
                RETURN caller,
                       best.rawDist + 1 AS dist,
                       CASE
                           WHEN caller.type IN {{WorkflowAdjacentTypeList}} THEN 'workflow'
                           ELSE 'dependency'
                       END AS bucket,
                       CASE
                           WHEN caller.type IN {{WorkflowAdjacentTypeList}} THEN 4
                           ELSE 3
                       END AS bucketRank,
                       best.pathNodes + [target] AS pathNodes,
                       best.pathRelationships + [{ type: type(implementationRel), confidence: implementationRel.confidence }] AS pathRelationships

                UNION

                WITH target, targetType
                WITH target, targetType
                WHERE targetType = 'Class'
                MATCH (target)-[implementationRel:Implements|Inherits]->(abstraction:CodeNode)
                MATCH (abstraction)-[:Contains*1..2]->(abstractionMember:CodeNode)
                MATCH path = (caller:CodeNode)-[:{{StructuralTraversalRelationships}}*1..8]->(abstractionMember)
                WHERE caller <> target
                  AND caller <> abstraction
                  AND caller <> abstractionMember
                WITH target,
                     abstraction,
                     implementationRel,
                     caller,
                     path,
                     length(path) AS rawDist,
                     [n IN nodes(path) | n] AS pathNodes,
                     [r IN relationships(path) | { type: type(r), confidence: r.confidence }] AS pathRelationships
                ORDER BY caller.id, rawDist ASC
                WITH target,
                     abstraction,
                     implementationRel,
                     caller,
                     collect({
                         rawDist: rawDist,
                         pathNodes: pathNodes,
                         pathRelationships: pathRelationships
                     })[0] AS best
                RETURN caller,
                       best.rawDist + 2 AS dist,
                       CASE
                           WHEN caller.type IN {{WorkflowAdjacentTypeList}} THEN 'workflow'
                           ELSE 'dependency'
                       END AS bucket,
                       CASE
                           WHEN caller.type IN {{WorkflowAdjacentTypeList}} THEN 4
                           ELSE 3
                       END AS bucketRank,
                       best.pathNodes + [abstraction, target] AS pathNodes,
                       best.pathRelationships + [{ type: 'Contains', confidence: 1.0 }, { type: type(implementationRel), confidence: implementationRel.confidence }] AS pathRelationships
            }
            WITH caller, dist, bucket, bucketRank, pathNodes, pathRelationships
            WHERE dist <= $depth
            ORDER BY caller.id, bucketRank ASC, dist ASC
            WITH caller, collect({
                dist: dist,
                bucket: bucket,
                bucketRank: bucketRank,
                pathNodes: pathNodes,
                pathRelationships: pathRelationships
            })[0] AS best
            RETURN caller,
                   best.dist AS dist,
                   best.bucket AS bucket,
                   best.bucketRank AS bucketRank,
                   best.pathNodes AS pathNodes,
                   best.pathRelationships AS pathRelationships
            ORDER BY dist ASC, bucketRank ASC, caller.name
            LIMIT 50
            """;

        var cursor = await session.RunAsync(cypher, new { nodeId, depth = maxDepth });
        var results = new List<ImpactPath>();

        await foreach (var record in cursor.WithCancellation(cancellationToken))
        {
            var caller = AnnotateImpactEvidenceNode(
                MapToCodeNode(record["caller"].As<INode>()),
                record["bucket"].As<string>());
            var dist = record["dist"].As<int>();
            var pathNodes = record["pathNodes"].As<List<INode>>();
            var pathRelationships = record["pathRelationships"].As<List<object>>();
            var steps = MapImpactPathSteps(pathNodes, pathRelationships, caller);

            results.Add(new ImpactPath(caller, dist, steps));
        }

        return results;
    }

    public async Task<IReadOnlyList<(CodeNode Node, int Distance)>> FindDownstreamAsync(
        string nodeId,
        int depth = 5,
        CancellationToken cancellationToken = default)
    {
        await using var session = _driver.AsyncSession();

        var cypher = $$"""
            MATCH (source:CodeNode {id: $nodeId})
            OPTIONAL MATCH (source)-[:Contains*0..2]->(sourceMember:CodeNode)
            WITH collect(DISTINCT source) + collect(DISTINCT sourceMember) AS sources
            UNWIND sources AS sourceNode
            WITH DISTINCT sourceNode, sources
            WHERE sourceNode IS NOT NULL
            MATCH path = (sourceNode)-[:{{StructuralTraversalRelationships}}*1..{{depth}}]->(downstream:CodeNode)
            WHERE none(source IN sources WHERE source = downstream)
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

        var cypher = $"""
            MATCH (caller:CodeNode)-[:{StructuralDependencyRelationships}]->(n:CodeNode)
            WHERE ($projectContextNormalized IS NULL OR n.projectContextNormalized = $projectContextNormalized)
              AND n.type IN ['Method', 'Class', 'Interface']
            RETURN n, count(caller) AS fanIn
            ORDER BY fanIn DESC
            LIMIT $limit
            """;

        var cursor = await session.RunAsync(cypher, new
        {
            projectContextNormalized = (object?)Normalize(projectContext),
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

        var cypher =
            "MATCH path = shortestPath(" +
            "(a:CodeNode {id: $fromId})-[:"
            + ConnectionRelationships +
            "*..10]-(b:CodeNode {id: $toId})) " +
            "RETURN [n IN nodes(path) | n] AS pathNodes, " +
            "[r IN relationships(path) | type(r)] AS relTypes";

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

        var cypher = $"""
            MATCH (n:CodeNode)
            WHERE n.type IN ['Method', 'Class']
              AND ($projectContextNormalized IS NULL OR n.projectContextNormalized = $projectContextNormalized)
              AND NOT ()-[:{StructuralDependencyRelationships}|Contains]->(n)
            RETURN n
            ORDER BY n.type, n.name
            LIMIT 100
            """;

        var cursor = await session.RunAsync(cypher, new { projectContextNormalized = (object?)Normalize(projectContext) });
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

        var fileRole = FileRoleExpression("n");
        var cypher = $@"
            MATCH (n:CodeNode)
            WHERE n.lineCount IS NOT NULL
              AND ($projectContextNormalized IS NULL OR n.projectContextNormalized = $projectContextNormalized)
              AND (
                (n.type = 'Class' AND n.lineCount > $classThreshold) OR
                (n.type = 'Method' AND n.lineCount > $methodThreshold)
              )
              AND {fileRole} IN ['Source', 'Unknown']
            RETURN n
            ORDER BY n.lineCount DESC
            LIMIT 50
            ";

        var cursor = await session.RunAsync(cypher, new
        {
            projectContextNormalized = (object?)Normalize(projectContext),
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

        var cypher =
            "MATCH (n:CodeNode {id: $nodeId}) " +
            "OPTIONAL MATCH (n)-[:Contains*0..2]->(member:CodeNode) " +
            "WITH n, [candidate IN collect(DISTINCT member) WHERE candidate IS NOT NULL] AS collectedMembers " +
            "WITH n, CASE WHEN size(collectedMembers) = 0 THEN [n] ELSE collectedMembers END AS members " +
            "UNWIND members AS callerTarget " +
            "OPTIONAL MATCH (caller:CodeNode)-[:"
            + StructuralTraversalRelationships +
            "]->(callerTarget) " +
            "WITH n, members, collect(DISTINCT caller)[..10] AS callers " +
            "UNWIND members AS calleeSource " +
            "OPTIONAL MATCH (calleeSource)-[:"
            + StructuralTraversalRelationships +
            "]->(callee:CodeNode) " +
            "WITH n, callers, collect(DISTINCT callee)[..10] AS callees " +
            "OPTIONAL MATCH (n)-[:Implements]->(iface:CodeNode) " +
            "WITH n, callers, callees, collect(DISTINCT iface)[..10] AS interfaces " +
            "RETURN n, callers, callees, interfaces";

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

        var fileRole = FileRoleExpression("n");
        var cypher = $@"
            MATCH (n:CodeNode)
            WHERE n.type = 'Class'
              AND n.lineCount IS NOT NULL
              AND ($projectContextNormalized IS NULL OR n.projectContextNormalized = $projectContextNormalized)
              AND n.lineCount > $lineThreshold
              AND {fileRole} IN ['Source', 'Unknown']
            CALL {{
                WITH n
                MATCH (caller:CodeNode)-[rel:{StructuralTraversalRelationships}]->(n)
                WHERE caller <> n
                RETURN caller,
                       CASE
                           WHEN caller.type IN {WorkflowAdjacentTypeList} THEN 'workflow'
                           WHEN type(rel) IN ['DependsOn', 'Implements', 'Inherits'] THEN 'dependency'
                           ELSE 'direct-class'
                       END AS bucket,
                       CASE
                           WHEN caller.type IN {WorkflowAdjacentTypeList} THEN 4
                           WHEN type(rel) IN ['DependsOn', 'Implements', 'Inherits'] THEN 3
                           ELSE 1
                       END AS bucketRank
                UNION
                WITH n
                MATCH (n)-[:Contains*1..2]->(member:CodeNode)<-[rel:{StructuralDependencyRelationships}]-(caller:CodeNode)
                WHERE caller <> n AND caller <> member
                RETURN caller,
                       CASE
                           WHEN caller.type IN {WorkflowAdjacentTypeList} THEN 'workflow'
                           WHEN type(rel) = 'DependsOn' THEN 'dependency'
                           ELSE 'member'
                       END AS bucket,
                       CASE
                           WHEN caller.type IN {WorkflowAdjacentTypeList} THEN 4
                           WHEN type(rel) = 'DependsOn' THEN 3
                           ELSE 2
                       END AS bucketRank
            }}
            WITH n, n.lineCount AS lineCount, caller, bucket, bucketRank
            WHERE caller IS NOT NULL
            ORDER BY caller.id, bucketRank ASC
            WITH n, lineCount, caller, collect({{ bucket: bucket, bucketRank: bucketRank }})[0] AS best
            WITH n,
                 lineCount,
                 count(DISTINCT caller) AS fanIn,
                 count(DISTINCT CASE WHEN best.bucket = 'direct-class' THEN caller END) AS directCallerCount,
                 count(DISTINCT CASE WHEN best.bucket = 'member' THEN caller END) AS memberCallerCount,
                 count(DISTINCT CASE WHEN best.bucket = 'dependency' THEN caller END) AS dependencyCallerCount,
                 count(DISTINCT CASE WHEN best.bucket = 'workflow' THEN caller END) AS heuristicCallerCount
            WHERE fanIn > $fanInThreshold
            WITH n,
                 lineCount,
                 fanIn,
                 directCallerCount,
                 memberCallerCount,
                 dependencyCallerCount,
                 heuristicCallerCount,
                 (directCallerCount * 4 + memberCallerCount * 3 + dependencyCallerCount * 2 + heuristicCallerCount) AS qualityScore
            RETURN n,
                   lineCount,
                   fanIn,
                   directCallerCount,
                   memberCallerCount,
                   dependencyCallerCount,
                   heuristicCallerCount,
                   qualityScore
            ORDER BY qualityScore DESC, lineCount DESC, fanIn DESC, n.name
            LIMIT 20
            ";

        var cursor = await session.RunAsync(cypher, new
        {
            projectContextNormalized = (object?)Normalize(projectContext),
            lineThreshold,
            fanInThreshold
        });

        var results = new List<(CodeNode, int, int)>();
        await foreach (var record in cursor.WithCancellation(cancellationToken))
        {
            var node = AnnotateGodClassNode(
                MapToCodeNode(record["n"].As<INode>()),
                record["directCallerCount"].As<int>(),
                record["memberCallerCount"].As<int>(),
                record["dependencyCallerCount"].As<int>(),
                record["heuristicCallerCount"].As<int>(),
                record["qualityScore"].As<int>());
            results.Add((
                node,
                record["lineCount"].As<int>(),
                record["fanIn"].As<int>()));
        }

        return results;
    }

    private static CodeNode AnnotateImpactEvidenceNode(CodeNode node, string bucket)
    {
        var properties = new Dictionary<string, string>(node.Properties, StringComparer.Ordinal)
        {
            ["impactEvidenceBucket"] = bucket
        };

        return node with { Properties = properties };
    }

    private static List<GraphPathStep> MapImpactPathSteps(
        List<INode> pathNodes,
        List<object> pathRelationships,
        CodeNode? annotatedCaller = null)
    {
        var steps = new List<GraphPathStep>(pathNodes.Count);

        for (var i = 0; i < pathNodes.Count; i++)
        {
            string? relationshipType = null;
            double? relationshipConfidence = null;

            if (i < pathRelationships.Count && pathRelationships[i] is IDictionary<string, object> rel)
            {
                relationshipType = rel.TryGetValue("type", out var typeValue)
                    ? typeValue?.ToString()
                    : null;

                if (rel.TryGetValue("confidence", out var confidenceValue) && confidenceValue is not null)
                {
                    relationshipConfidence = confidenceValue switch
                    {
                        double d => d,
                        float f => f,
                        long l => l,
                        int n => n,
                        _ when double.TryParse(confidenceValue.ToString(), out var parsed) => parsed,
                        _ => null
                    };
                }
            }

            var node = i == 0 && annotatedCaller is not null
                ? annotatedCaller
                : MapToCodeNode(pathNodes[i]);

            steps.Add(new GraphPathStep(
                node,
                relationshipType,
                relationshipConfidence));
        }

        return steps;
    }

    private static CodeNode AnnotateGodClassNode(
        CodeNode node,
        int directCallerCount,
        int memberCallerCount,
        int dependencyCallerCount,
        int heuristicCallerCount,
        int qualityScore)
    {
        var properties = new Dictionary<string, string>(node.Properties, StringComparer.Ordinal)
        {
            ["godClassDirectCallerCount"] = directCallerCount.ToString(),
            ["godClassMemberCallerCount"] = memberCallerCount.ToString(),
            ["godClassDependencyCallerCount"] = dependencyCallerCount.ToString(),
            ["godClassHeuristicCallerCount"] = heuristicCallerCount.ToString(),
            ["godClassQualityScore"] = qualityScore.ToString()
        };

        return node with { Properties = properties };
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
              AND ($projectContextNormalized IS NULL OR a.projectContextNormalized = $projectContextNormalized)
            WITH DISTINCT a.namespace AS nsA, b.namespace AS nsB
            MATCH (c:CodeNode)-[:Calls|Uses|DependsOn]->(d:CodeNode)
            WHERE c.namespace = nsB AND d.namespace = nsA
              AND c.namespace <> d.namespace
            RETURN DISTINCT nsA AS fromNamespace, nsB AS toNamespace
            ORDER BY fromNamespace, toNamespace
            LIMIT 50
            """;

        var cursor = await session.RunAsync(cypher, new { projectContextNormalized = (object?)Normalize(projectContext) });
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
        var architecture = await LoadArchitectureRulesAsync(session, projectContext, cancellationToken);
        var ruleCypher = BuildArchitectureRuleCypher(architecture);
        var cypher = $"""
            MATCH (source:CodeNode)-[:Calls|Uses|DependsOn]->(target:CodeNode)
            WHERE ($projectContextNormalized IS NULL OR source.projectContextNormalized = $projectContextNormalized)
              AND source.namespace IS NOT NULL
              AND target.namespace IS NOT NULL
              AND ({ruleCypher.WhereClause})
            WITH source, target,
                 CASE
                 {ruleCypher.CaseClause}
                   ELSE source.namespace + ' → ' + target.namespace
                 END AS violation
            RETURN source, target, violation
            ORDER BY violation, source.name, target.name
            LIMIT 50
            """;

        var parameters = ruleCypher.Parameters;
        parameters["projectContextNormalized"] = Normalize(projectContext);
        var cursor = await session.RunAsync(cypher, parameters);
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

    public async Task<IReadOnlyList<DependencySmellPath>> FindSmellPathsAsync(
        string? projectContext = null,
        int maxDepth = 4,
        CancellationToken cancellationToken = default)
    {
        await using var session = _driver.AsyncSession();
        var architecture = await LoadArchitectureRulesAsync(session, projectContext, cancellationToken);
        var ruleCypher = BuildArchitectureRuleCypher(architecture);

        var sourceRole = FileRoleExpression("source");
        var targetRole = FileRoleExpression("target");
        var cypher = $$"""
            MATCH path = (source:CodeNode)-[:Calls|Uses|DependsOn*1..{{Math.Clamp(maxDepth, 1, 6)}}]->(target:CodeNode)
            WHERE ($projectContextNormalized IS NULL OR source.projectContextNormalized = $projectContextNormalized)
              AND source <> target
              AND source.namespace IS NOT NULL
              AND target.namespace IS NOT NULL
              AND {{sourceRole}} IN ['Source', 'Unknown']
              AND {{targetRole}} IN ['Source', 'Unknown']
              AND ({{ruleCypher.WhereClause}})
            WITH source, target, path, length(path) AS dist,
                 CASE
                   WHEN source.namespace CONTAINS 'Core' AND target.namespace CONTAINS 'Infrastructure' THEN 'Core → Infrastructure'
                   WHEN source.namespace CONTAINS 'Core' AND target.namespace CONTAINS 'McpServer' THEN 'Core → Presentation'
                   WHEN source.namespace CONTAINS 'Core' AND target.namespace CONTAINS 'Application' THEN 'Core → Application'
                   WHEN source.namespace CONTAINS 'Application' AND target.namespace CONTAINS 'Infrastructure' THEN 'Application → Infrastructure'
                   WHEN source.namespace CONTAINS 'Application' AND target.namespace CONTAINS 'McpServer' THEN 'Application → Presentation'
                   ELSE 'Presentation → Infrastructure'
                 END AS violation
            WITH source, target, path, dist,
                 CASE
                 {{ruleCypher.CaseClause}}
                   ELSE violation
                 END AS violation
            ORDER BY violation, source.id, target.id, dist ASC
            WITH violation, source, target, collect(path)[0] AS shortestPath, min(dist) AS dist
            RETURN violation,
                   source,
                   target,
                   dist,
                   [n IN nodes(shortestPath) | n] AS pathNodes,
                   [r IN relationships(shortestPath) | { type: type(r), confidence: r.confidence }] AS pathRelationships
            ORDER BY dist ASC, violation, source.name, target.name
            LIMIT 50
            """;

        var parameters = ruleCypher.Parameters;
        parameters["projectContextNormalized"] = Normalize(projectContext);
        var cursor = await session.RunAsync(cypher, parameters);

        var results = new List<DependencySmellPath>();

        await foreach (var record in cursor.WithCancellation(cancellationToken))
        {
            var source = MapToCodeNode(record["source"].As<INode>());
            var target = MapToCodeNode(record["target"].As<INode>());
            var dist = record["dist"].As<int>();
            var pathNodes = record["pathNodes"].As<List<INode>>();
            var pathRelationships = record["pathRelationships"].As<List<object>>();
            var steps = new List<GraphPathStep>(pathNodes.Count);

            for (var i = 0; i < pathNodes.Count; i++)
            {
                string? relationshipType = null;
                double? relationshipConfidence = null;

                if (i < pathRelationships.Count && pathRelationships[i] is IDictionary<string, object> rel)
                {
                    relationshipType = rel.TryGetValue("type", out var typeValue)
                        ? typeValue?.ToString()
                        : null;

                    if (rel.TryGetValue("confidence", out var confidenceValue) && confidenceValue is not null)
                    {
                        relationshipConfidence = confidenceValue switch
                        {
                            double d => d,
                            float f => f,
                            long l => l,
                            int n => n,
                            _ when double.TryParse(confidenceValue.ToString(), out var parsed) => parsed,
                            _ => null
                        };
                    }
                }

                steps.Add(new GraphPathStep(
                    MapToCodeNode(pathNodes[i]),
                    relationshipType,
                    relationshipConfidence));
            }

            results.Add(new DependencySmellPath(
                record["violation"].As<string>(),
                source,
                target,
                dist,
                steps));
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
              AND ($projectContextNormalized IS NULL OR n.projectContextNormalized = $projectContextNormalized)
            WITH n,
                 CASE WHEN n.type = 'Namespace' THEN 1 ELSE 0 END AS namespaceRank,
                 CASE
                   WHEN coalesce(n.namespaceNormalized CONTAINS 'test', false) THEN 1
                   WHEN coalesce(n.filePathNormalized CONTAINS 'test', false) THEN 1
                   ELSE 0
                 END AS testRank
            RETURN n, n.changeCount AS changeCount
            ORDER BY testRank ASC, namespaceRank ASC, changeCount DESC, coalesce(n.lineCount, 0) DESC, n.name
            LIMIT 50
            """;

        var cursor = await session.RunAsync(cypher, new
        {
            projectContextNormalized = (object?)Normalize(projectContext),
            threshold
        });

        var results = new List<(CodeNode, int)>();

        await foreach (var record in cursor.WithCancellation(cancellationToken))
            results.Add((MapToCodeNode(record["n"].As<INode>()), record["changeCount"].As<int>()));

        return results;
    }
}
