namespace CodeMeridian.Evolution.Infrastructure.Sensors;

public sealed class CodeMeridianSensorOptions
{
    public bool Enabled { get; set; }

    public string BaseUrl { get; set; } = "http://localhost:5100";

    public string ApiKey { get; set; } = string.Empty;

    public string ProjectContext { get; set; } = "CodeMeridian";

    public string TargetProjectId { get; set; } = "codemeridian";

    public int MaximumNodes { get; set; } = 50;
}
