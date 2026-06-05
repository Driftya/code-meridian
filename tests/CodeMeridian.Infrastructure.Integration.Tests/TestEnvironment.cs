using System.Globalization;
using CodeMeridian.Infrastructure.Configuration;

namespace CodeMeridian.Infrastructure.Integration.Tests;

internal static class TestEnvironment
{
    private const string DefaultUsername = "neo4j";

    internal static Neo4jOptions? TryGetNeo4jOptions()
    {
        var env = LoadMergedEnvironment();

        var uri = FirstNonEmpty(
            Environment.GetEnvironmentVariable("Neo4j__Uri"),
            Environment.GetEnvironmentVariable("NEO4J__URI"),
            Environment.GetEnvironmentVariable("NEO4J_URI"),
            env.GetValueOrDefault("Neo4j__Uri"),
            env.GetValueOrDefault("NEO4J__URI"),
            env.GetValueOrDefault("NEO4J_URI"));

        if (string.IsNullOrWhiteSpace(uri))
        {
            var host = FirstNonEmpty(
                Environment.GetEnvironmentVariable("NEO4J_HOST"),
                env.GetValueOrDefault("NEO4J_HOST"));

            var port = FirstNonEmpty(
                Environment.GetEnvironmentVariable("NEO4J_BOLT_PORT"),
                env.GetValueOrDefault("NEO4J_BOLT_PORT"));

            if (!string.IsNullOrWhiteSpace(host))
            {
                var resolvedPort = int.TryParse(port, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedPort)
                    ? parsedPort
                    : 7687;
                uri = $"bolt://{host}:{resolvedPort}";
            }
        }

        var username = FirstNonEmpty(
            Environment.GetEnvironmentVariable("Neo4j__Username"),
            Environment.GetEnvironmentVariable("NEO4J__USERNAME"),
            Environment.GetEnvironmentVariable("NEO4J_USERNAME"),
            env.GetValueOrDefault("Neo4j__Username"),
            env.GetValueOrDefault("NEO4J__USERNAME"),
            env.GetValueOrDefault("NEO4J_USERNAME")) ?? DefaultUsername;

        var password = FirstNonEmpty(
            Environment.GetEnvironmentVariable("Neo4j__Password"),
            Environment.GetEnvironmentVariable("NEO4J__PASSWORD"),
            Environment.GetEnvironmentVariable("NEO4J_PASSWORD"),
            env.GetValueOrDefault("Neo4j__Password"),
            env.GetValueOrDefault("NEO4J__PASSWORD"),
            env.GetValueOrDefault("NEO4J_PASSWORD"));

        if (string.IsNullOrWhiteSpace(uri) || string.IsNullOrWhiteSpace(password))
            return null;

        var embeddingDimensions = ReadInt(
            FirstNonEmpty(
                Environment.GetEnvironmentVariable("Neo4j__EmbeddingDimensions"),
                Environment.GetEnvironmentVariable("NEO4J__EMBEDDINGDIMENSIONS"),
                env.GetValueOrDefault("Neo4j__EmbeddingDimensions"),
                env.GetValueOrDefault("NEO4J__EMBEDDINGDIMENSIONS")),
            fallback: 1536);

        return new Neo4jOptions
        {
            Uri = uri,
            Username = username,
            Password = password,
            EmbeddingDimensions = embeddingDimensions
        };
    }

    internal static string? TryGetProjectContext()
    {
        var env = LoadMergedEnvironment();
        return FirstNonEmpty(
            Environment.GetEnvironmentVariable("CodeMeridian_Project"),
            Environment.GetEnvironmentVariable("CODEMERIDIAN_PROJECT"),
            env.GetValueOrDefault("CodeMeridian_Project"),
            env.GetValueOrDefault("CODEMERIDIAN_PROJECT"));
    }

    internal static string? FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "CodeMeridian.sln")))
                return current.FullName;

            current = current.Parent;
        }

        return null;
    }

    private static IReadOnlyDictionary<string, string> LoadMergedEnvironment()
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var repoRoot = FindRepoRoot();
        if (repoRoot is null)
            return values;

        var envFile = FindDotEnv(new DirectoryInfo(repoRoot));
        if (envFile is null || !envFile.Exists)
            return values;

        foreach (var line in File.ReadAllLines(envFile.FullName))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#'))
                continue;

            if (trimmed.StartsWith("export ", StringComparison.OrdinalIgnoreCase))
                trimmed = trimmed["export ".Length..].TrimStart();

            var separator = trimmed.IndexOf('=');
            if (separator <= 0)
                continue;

            var key = trimmed[..separator].Trim();
            var value = trimmed[(separator + 1)..].Trim();

            if (string.IsNullOrWhiteSpace(key) || values.ContainsKey(key))
                continue;

            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
                value = value[1..^1].Replace("\\\"", "\"");
            else if (value.Length >= 2 && value[0] == '\'' && value[^1] == '\'')
                value = value[1..^1];

            values[key] = value;
        }

        return values;
    }

    private static FileInfo? FindDotEnv(DirectoryInfo directory)
    {
        for (var current = directory; current is not null; current = current.Parent)
        {
            var envFile = new FileInfo(Path.Combine(current.FullName, ".env"));
            if (envFile.Exists)
                return envFile;
        }

        return null;
    }

    private static int ReadInt(string? value, int fallback) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}
