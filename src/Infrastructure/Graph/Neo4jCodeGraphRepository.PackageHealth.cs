using CodeMeridian.Core.CodeGraph;
using Neo4j.Driver;

namespace CodeMeridian.Infrastructure.Graph;

public sealed partial class Neo4jCodeGraphRepository
{
    public async Task<PackageReferenceHealth> GetPackageReferenceHealthAsync(string? projectContext = null, CancellationToken cancellationToken = default)
    {
        await using var session = _driver.AsyncSession();
        var cursor = await session.RunAsync("""
            MATCH (n:PackageReference) WHERE $project IS NULL OR n.projectContextNormalized = $project
            RETURN CASE WHEN n.pending THEN 'pending' ELSE coalesce(n.status, 'source_not_indexed') END AS status, count(*) AS count
            """, new { project = Normalize(projectContext) });
        var counts = new Dictionary<string, long>();
        await foreach (var record in cursor.WithCancellation(cancellationToken)) counts[record["status"].As<string>()] = record["count"].As<long>();
        cursor = await session.RunAsync("MATCH (n:PackageManifest) WHERE $project IS NULL OR n.projectContextNormalized = $project RETURN n LIMIT 100",
            new { project = Normalize(projectContext) });
        var diagnostics = new List<string>();
        await foreach (var record in cursor.WithCancellation(cancellationToken))
            diagnostics.AddRange(ReadPackageJson<PackageReferenceSnapshot>(record["n"].As<INode>(), "data").Diagnostics.Take(10));
        return new(counts, diagnostics.Take(100).ToArray());
    }
}
