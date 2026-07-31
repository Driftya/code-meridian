namespace CodeMeridian.Evolution.Application.Reasoning;

public interface IReasoningProvider
{
    string Id { get; }

    Task<ProviderCapabilities> ProbeAsync(CancellationToken cancellationToken = default);

    Task<ReasoningResult> InvokeAsync(
        ReasoningRequest request,
        CancellationToken cancellationToken = default);

    Task CancelAsync(Guid invocationId, CancellationToken cancellationToken = default);
}
