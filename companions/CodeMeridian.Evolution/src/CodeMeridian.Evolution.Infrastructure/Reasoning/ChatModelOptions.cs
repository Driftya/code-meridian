namespace CodeMeridian.Evolution.Infrastructure.Reasoning;

public sealed class ChatModelOptions
{
    public bool Enabled { get; set; }

    public string ProviderId { get; set; } = "chat-model";

    public string Endpoint { get; set; } = "http://localhost:11434/v1/chat/completions";

    public string ApiKey { get; set; } = string.Empty;

    public string Model { get; set; } = string.Empty;

    public int MaximumResponseBytes { get; set; } = 262_144;
}
