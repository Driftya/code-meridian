namespace CodeMeridian.Evolution.Infrastructure.Sensors;

public sealed class InternetFeedOptions
{
    public bool Enabled { get; set; }

    public string ProjectId { get; set; } = "meridian-evolution";

    public string[] FeedUrls { get; set; } = [];

    public string[] AllowedHosts { get; set; } = [];

    public int MaximumItemsPerFeed { get; set; } = 10;

    public int MaximumResponseBytes { get; set; } = 262_144;
}
