namespace CodeMeridian.Evolution.Worker;

public sealed class EvolutionWorkerOptions
{
    public TimeSpan SensorInterval { get; set; } = TimeSpan.FromSeconds(30);

    public bool AutonomousCognitionEnabled { get; set; } = true;

    public string ReasoningProviderId { get; set; } = "fake";

    public string ReasoningRole { get; set; } = "researcher";

    public string[] ProjectIds { get; set; } = ["meridian-evolution", "codemeridian"];

    public int MaximumAttentionItems { get; set; } = 8;
}
