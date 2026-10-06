namespace CodeMeridian.Tooling.Configuration;

public sealed class CodeMeridianSqlOptions
{
    public bool Enabled { get; set; }
    public string DefaultDialect { get; set; } = "postgresql";
    public string DatabaseScope { get; set; } = "default";
    public string[]? SearchPath { get; set; }
    public CodeMeridianSqlSourceOptions[]? Sources { get; set; }
}
