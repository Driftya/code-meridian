using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using CodeMeridian.Evolution.Application.Sensors;

namespace CodeMeridian.Evolution.Infrastructure.Sensors;

public sealed class HumanPromptSensor(TimeProvider timeProvider) : ISensor, IPromptReceiver
{
    private readonly ConcurrentQueue<PromptInput> pending = new();

    public string Id => "human-prompt";

    public string SensorId => Id;

    public string DisplayName => "Human prompt";

    public Task EnqueueAsync(
        PromptInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.Text);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.Actor);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.ProjectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.IdempotencyKey);
        cancellationToken.ThrowIfCancellationRequested();
        pending.Enqueue(input);
        return Task.CompletedTask;
    }

    public Task<SensorHealth> CheckHealthAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new SensorHealth(
            IsHealthy: true,
            "ready",
            timeProvider.GetUtcNow()));
    }

    public Task<IReadOnlyList<SensorObservation>> CollectAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var observations = new List<SensorObservation>();

        while (pending.TryDequeue(out var input))
        {
            var text = input.Text.Length <= 4_000
                ? input.Text
                : input.Text[..4_000];
            observations.Add(new SensorObservation(
                StableId(input.IdempotencyKey),
                "human-prompt",
                $"{input.Actor}: {text}",
                "information",
                timeProvider.GetUtcNow(),
                0.95m)
            {
                ProjectId = input.ProjectId,
                TrustLevel = "human-supplied"
            });
        }

        return Task.FromResult<IReadOnlyList<SensorObservation>>(
            Array.AsReadOnly(observations.ToArray()));
    }

    private static string StableId(string idempotencyKey)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(idempotencyKey));
        return $"prompt:{Convert.ToHexString(hash).ToLowerInvariant()}";
    }
}
