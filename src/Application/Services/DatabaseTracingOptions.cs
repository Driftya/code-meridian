namespace CodeMeridian.Application.Services;

public sealed class DatabaseTracingOptions
{
    public const string SectionName = "DatabaseTracing";

    public bool Enabled { get; set; } = true;
    public int ConfigurationVersion { get; set; } = 1;
    public int MaxTablesPerOperation { get; set; } = 6;
    public List<DatabaseTracingPresetOptions> Presets { get; set; } = DatabaseTracingPresetDefaults.Create();
}
