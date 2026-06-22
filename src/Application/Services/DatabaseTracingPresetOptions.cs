namespace CodeMeridian.Application.Services;

public sealed class DatabaseTracingPresetOptions
{
    public string Id { get; set; } = string.Empty;
    public string Strategy { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public List<string> Languages { get; set; } = [];
    public List<string> ReadMethods { get; set; } = [];
    public List<string> WriteMethods { get; set; } = [];
    public List<int> StatementArgumentIndexes { get; set; } = [];
    public List<int> SqlArgumentIndexes { get; set; } = [0];
    public List<string> ReceiverTextHints { get; set; } = [];
    public List<string> ImportModuleHints { get; set; } = [];
    public List<string> CommandCreationTypeHints { get; set; } = [];
    public List<string> StatementTextProperties { get; set; } = [];
    public List<string> CommandTextProperties { get; set; } = ["CommandText"];
    public List<string> TableSources { get; set; } = [];

    public IReadOnlyList<int> GetEffectiveStatementArgumentIndexes() =>
        StatementArgumentIndexes.Count > 0 ? StatementArgumentIndexes : SqlArgumentIndexes;

    public IReadOnlyList<string> GetEffectiveStatementTextProperties() =>
        StatementTextProperties.Count > 0 ? StatementTextProperties : CommandTextProperties;
}
