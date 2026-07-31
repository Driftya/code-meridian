namespace CodeMeridian.Evolution.Application.Sensors;

public interface IPromptReceiver
{
    string SensorId { get; }

    Task EnqueueAsync(
        PromptInput input,
        CancellationToken cancellationToken = default);
}
