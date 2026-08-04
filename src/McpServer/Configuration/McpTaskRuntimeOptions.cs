namespace CodeMeridian.McpServer.Configuration;

public sealed class McpTaskRuntimeOptions
{
    public const string SectionName = "Mcp:Tasks";

    public bool Enabled { get; init; } = true;
    public int PollIntervalMilliseconds { get; init; } = 500;
    public int TimeToLiveMinutes { get; init; } = 30;
    public int MaxActiveTasks { get; init; } = 4;
    public int MaxDurationSeconds { get; init; } = 30 * 60;
    public int MaxResultBytes { get; init; } = 128 * 1024;

    public TimeSpan TimeToLive => TimeSpan.FromMinutes(Math.Clamp(TimeToLiveMinutes, 1, 24 * 60));
    public TimeSpan MaxDuration => TimeSpan.FromSeconds(Math.Clamp(MaxDurationSeconds, 1, 24 * 60 * 60));
    public int BoundedPollIntervalMilliseconds => Math.Clamp(PollIntervalMilliseconds, 100, 60_000);
    public int BoundedMaxActiveTasks => Math.Clamp(MaxActiveTasks, 1, 100);
    public int BoundedMaxResultBytes => Math.Clamp(MaxResultBytes, 1024, 4 * 1024 * 1024);
}
