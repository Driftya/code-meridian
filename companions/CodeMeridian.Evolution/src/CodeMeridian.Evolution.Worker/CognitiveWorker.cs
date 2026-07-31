using CodeMeridian.Evolution.Application.Cognition;
using CodeMeridian.Evolution.Application.Ledger;
using CodeMeridian.Evolution.Application.Sensors;
using Microsoft.Extensions.Options;

namespace CodeMeridian.Evolution.Worker;

public sealed partial class CognitiveWorker(
    CognitiveLedgerService ledgerService,
    SensorRegistry sensorRegistry,
    SensorRunner sensorRunner,
    CognitiveMind cognitiveMind,
    IOptions<EvolutionWorkerOptions> options,
    ILogger<CognitiveWorker> logger) : BackgroundService
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Information,
        Message = "Sensor {SensorId} observed {ObservedCount} item(s); appended {AppendedCount}.")]
    private static partial void LogSensorRun(
        ILogger logger,
        string sensorId,
        int observedCount,
        int appendedCount);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Error,
        Message = "Sensor {SensorId} failed.")]
    private static partial void LogSensorFailure(
        ILogger logger,
        string sensorId,
        Exception exception);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Information,
        Message = "Cognitive cycle for {ProjectId} completed with status {Status}.")]
    private static partial void LogCognitiveCycle(
        ILogger logger,
        string projectId,
        CognitiveCycleStatus status);

    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Error,
        Message = "Cognitive cycle for {ProjectId} failed.")]
    private static partial void LogCognitiveCycleFailure(
        ILogger logger,
        string projectId,
        Exception exception);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await ledgerService.InitializeAsync(stoppingToken).ConfigureAwait(false);
        using var timer = new PeriodicTimer(options.Value.SensorInterval);

        do
        {
            foreach (var sensor in sensorRegistry.List())
            {
                try
                {
                    var result = await sensorRunner
                        .RunAsync(sensor.Id, stoppingToken)
                        .ConfigureAwait(false);
                    LogSensorRun(
                        logger,
                        result.SensorId,
                        result.ObservedCount,
                        result.AppendedCount);
                }
                catch (Exception exception) when (
                    exception is not OperationCanceledException &&
                    !stoppingToken.IsCancellationRequested)
                {
                    LogSensorFailure(logger, sensor.Id, exception);
                }
            }

            if (options.Value.AutonomousCognitionEnabled)
            {
                await RunCognitiveCyclesAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    private async Task RunCognitiveCyclesAsync(CancellationToken stoppingToken)
    {
        foreach (var projectId in options.Value.ProjectIds)
        {
            try
            {
                var result = await cognitiveMind
                    .RunCycleAsync(
                        new CognitiveCycleRequest(
                            options.Value.ReasoningProviderId,
                            options.Value.ReasoningRole,
                            projectId,
                            Goal: null,
                            options.Value.MaximumAttentionItems,
                            Force: false),
                        stoppingToken)
                    .ConfigureAwait(false);
                LogCognitiveCycle(logger, projectId, result.Status);
            }
            catch (Exception exception) when (
                exception is not OperationCanceledException &&
                !stoppingToken.IsCancellationRequested)
            {
                LogCognitiveCycleFailure(logger, projectId, exception);
            }
        }
    }
}
