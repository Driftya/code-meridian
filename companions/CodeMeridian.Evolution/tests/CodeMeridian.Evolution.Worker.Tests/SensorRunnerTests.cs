using CodeMeridian.Evolution.Application.Ledger;
using CodeMeridian.Evolution.Application.Governance;
using CodeMeridian.Evolution.Application.Sensors;
using CodeMeridian.Evolution.Infrastructure.Journal;
using CodeMeridian.Evolution.Infrastructure.Sensors;

namespace CodeMeridian.Evolution.Worker.Tests;

public sealed class SensorRunnerTests
{
    [Fact]
    public async Task RepeatedSensorRunDoesNotDuplicateSameObservation()
    {
        var store = new InMemoryJournalStore();
        var ledger = new CognitiveLedgerService(store, TimeProvider.System);
        await ledger.InitializeAsync(CancellationToken.None);
        var sensor = new StableSensor();
        var runner = new SensorRunner(new SensorRegistry([sensor]), ledger);

        var first = await runner.RunAsync(sensor.Id, CancellationToken.None);
        var repeated = await runner.RunAsync(sensor.Id, CancellationToken.None);

        Assert.Equal(1, first.AppendedCount);
        Assert.Equal(0, repeated.AppendedCount);
        Assert.Equal(2, (await ledger.GetJournalAsync(
            CancellationToken.None)).Count);
    }

    [Fact]
    public async Task PausedGovernanceSkipsSensorCollection()
    {
        var store = new InMemoryJournalStore();
        var ledger = new CognitiveLedgerService(store, TimeProvider.System);
        await ledger.InitializeAsync(CancellationToken.None);
        await ledger.SetPausedAsync(
            isPaused: true,
            new GovernanceCommand(
                "worker-test",
                "Pause sensor work.",
                "pause:worker-test"),
            CancellationToken.None);
        var sensor = new StableSensor();
        var runner = new SensorRunner(new SensorRegistry([sensor]), ledger);

        var result = await runner.RunAsync(sensor.Id, CancellationToken.None);

        Assert.False(result.Health.IsHealthy);
        Assert.Equal("governance-paused", result.Health.Status);
        Assert.Equal(0, result.ObservedCount);
        Assert.Equal(2, (await ledger.GetJournalAsync(CancellationToken.None)).Count);
    }

    private sealed class StableSensor : ISensor
    {
        private static readonly DateTimeOffset ObservedAt = DateTimeOffset.Parse(
            "2026-07-28T12:00:00Z",
            System.Globalization.CultureInfo.InvariantCulture);

        public string Id => "stable";

        public string DisplayName => "Stable test sensor";

        public Task<SensorHealth> CheckHealthAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new SensorHealth(true, "ready", ObservedAt));
        }

        public Task<IReadOnlyList<SensorObservation>> CollectAsync(
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<SensorObservation> observations =
            [
                new(
                    "stable:observation",
                    "test",
                    "Stable observation.",
                    "information",
                    ObservedAt,
                    1m)
            ];
            return Task.FromResult(observations);
        }
    }
}
