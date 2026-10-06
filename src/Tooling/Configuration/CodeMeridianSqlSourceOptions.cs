namespace CodeMeridian.Tooling.Configuration;

public sealed class CodeMeridianSqlSourceOptions
{
    public required string Pattern { get; set; }
    public required string Dialect { get; set; }
    public string? DatabaseScope { get; set; }
}
