using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CodeMeridian.Core.Projects;
using CodeMeridian.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using Neo4j.Driver;

namespace CodeMeridian.Infrastructure.Graph;

public sealed class Neo4jProjectProfileRepository : IProjectProfileRepository, IAsyncDisposable
{
    private readonly IDriver _driver;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Neo4jProjectProfileRepository(IOptions<Neo4jOptions> options)
    {
        var settings = options.Value;
        _driver = GraphDatabase.Driver(settings.Uri, AuthTokens.Basic(settings.Username, settings.Password),
            config => config.WithConnectionTimeout(TimeSpan.FromSeconds(settings.ConnectionTimeoutSeconds)));
    }

    internal Neo4jProjectProfileRepository(IDriver driver) => _driver = driver;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var session = _driver.AsyncSession();
        foreach (var query in new[]
        {
            "CREATE CONSTRAINT project_profile_project IF NOT EXISTS FOR (s:ProjectProfileState) REQUIRE s.project IS UNIQUE",
            "CREATE CONSTRAINT project_inventory_identity IF NOT EXISTS FOR (f:ProjectInventoryFile) REQUIRE (f.project, f.path) IS UNIQUE"
        })
        {
            cancellationToken.ThrowIfCancellationRequested();
            await (await session.RunAsync(query)).ConsumeAsync();
        }
    }

    public async Task<long> BeginAsync(string project, CancellationToken cancellationToken = default)
    {
        ValidateProject(project);
        await using var session = _driver.AsyncSession();
        return await session.ExecuteWriteAsync(async tx =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var record = await (await tx.RunAsync("""
                MERGE (s:ProjectProfileState {project: $project})
                ON CREATE SET s.createdAt = timestamp()
                SET s.nextGeneration = coalesce(s.nextGeneration, 0) + 1, s.updatedAt = timestamp()
                RETURN s.nextGeneration AS generation
                """, new { project })).SingleAsync();
            return record["generation"].As<long>();
        });
    }

    public async Task PublishAsync(ProjectProfileSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        if (snapshot.Validate() is { } error) throw new ArgumentException(error, nameof(snapshot));
        var inputJson = JsonSerializer.Serialize(snapshot, JsonOptions);
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(inputJson)));
        var profileJson = JsonSerializer.Serialize(snapshot.Profile with { EvidenceSource = "uploaded_discovery" }, JsonOptions);
        var project = snapshot.Profile.Project;
        await using var session = _driver.AsyncSession();
        await session.ExecuteWriteAsync(async tx =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            // A dependent property update acquires the project lock before reading generation state.
            var cursor = await tx.RunAsync("""
                MATCH (s:ProjectProfileState {project: $project})
                SET s.lockRevision = coalesce(s.lockRevision, 0) + 1
                RETURN s.nextGeneration AS latest, coalesce(s.generation, 0) AS published, s.fingerprint AS fingerprint
                """, new { project });
            if (!await cursor.FetchAsync())
                throw new ProjectProfileConflictException("Reserve a generation before publishing a project profile.");
            var record = cursor.Current;
            var latest = record["latest"].As<long>();
            var published = record["published"].As<long>();
            var oldFingerprint = record["fingerprint"].As<string?>();
            await cursor.ConsumeAsync();
            if (snapshot.Generation == published && oldFingerprint == fingerprint) return;
            if (snapshot.Generation <= published || snapshot.Generation > latest)
                throw new ProjectProfileConflictException("The generation is stale, unreserved, or already published with different content.");

            await (await tx.RunAsync("""
                MATCH (f:ProjectInventoryFile {project: $project}) DELETE f
                """, new { project })).ConsumeAsync();

            foreach (var batch in snapshot.Files.Chunk(1000))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var files = batch.Select(file => new Dictionary<string, object> { ["path"] = file.Path, ["kind"] = file.Kind }).ToArray();
                await (await tx.RunAsync("""
                    UNWIND $files AS file
                    CREATE (:ProjectInventoryFile {project: $project, path: file.path, kind: file.kind,
                        generation: $generation, createdAt: timestamp(), updatedAt: timestamp()})
                    """, new { project, files, generation = snapshot.Generation })).ConsumeAsync();
            }
            cancellationToken.ThrowIfCancellationRequested();
            await (await tx.RunAsync("""
                MATCH (s:ProjectProfileState {project: $project})
                SET s.generation = $generation, s.profileJson = $profileJson,
                    s.fingerprint = $fingerprint, s.publishedAt = timestamp(), s.updatedAt = timestamp()
                """, new { project, generation = snapshot.Generation, profileJson, fingerprint })).ConsumeAsync();
        });
    }

    public async Task<StoredProjectProfile?> GetAsync(string project, string? targetPath = null, CancellationToken cancellationToken = default)
    {
        ValidateProject(project);
        if (targetPath is not null) targetPath = ProjectProfilePath.Normalize(targetPath);
        await using var session = _driver.AsyncSession();
        return await session.ExecuteReadAsync(async tx =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cursor = await tx.RunAsync("""
                MATCH (s:ProjectProfileState {project: $project})
                WHERE s.profileJson IS NOT NULL
                OPTIONAL MATCH (f:ProjectInventoryFile {project: $project, path: $targetPath, generation: s.generation})
                RETURN s.profileJson AS profile, s.generation AS generation, s.nextGeneration AS latest,
                    s.publishedAt AS publishedAt, f.path AS path, f.kind AS kind
                """, new { project, targetPath });
            if (!await cursor.FetchAsync()) return null;
            var record = cursor.Current;
            var profile = JsonSerializer.Deserialize<ProjectProfile>(record["profile"].As<string>(), JsonOptions)
                ?? throw new InvalidOperationException("Stored project profile is invalid.");
            var path = record["path"].As<string?>();
            var target = path is null ? null : new ProjectInventoryFile(path, record["kind"].As<string>());
            return new StoredProjectProfile(record["generation"].As<long>(), record["latest"].As<long>(),
                DateTimeOffset.FromUnixTimeMilliseconds(record["publishedAt"].As<long>()), profile, target);
        });
    }

    private static void ValidateProject(string project)
    {
        if (string.IsNullOrWhiteSpace(project) || project.Length > 200 || project != project.Trim() || project.Any(char.IsControl))
            throw new ArgumentException("A nonempty project name of at most 200 characters is required.", nameof(project));
    }

    public async ValueTask DisposeAsync() => await _driver.DisposeAsync();
}
