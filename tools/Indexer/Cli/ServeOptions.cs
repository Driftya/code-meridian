namespace CodeMeridian.Indexer.Cli;

internal sealed record ServeOptions(
    DirectoryInfo RootDirectory,
    string Host,
    int Port,
    int Neo4jHttpPort,
    int Neo4jBoltPort,
    string ComposeFileName,
    string Image,
    bool Force,
    bool Start)
{
    public const string DefaultHost = "localhost";
    public const int DefaultPort = 5100;
    public const int DefaultNeo4jHttpPort = 47474;
    public const int DefaultNeo4jBoltPort = 47687;
    public const string DefaultComposeFileName = "docker-compose.codemeridian.yml";
    public const string DefaultImage = "ghcr.io/driftya/codemeridian-mcp:latest";

    public static ServeOptions CreateDefault(DirectoryInfo rootDirectory) =>
        new(
            rootDirectory,
            DefaultHost,
            DefaultPort,
            DefaultNeo4jHttpPort,
            DefaultNeo4jBoltPort,
            DefaultComposeFileName,
            DefaultImage,
            Force: false,
            Start: true);
}
