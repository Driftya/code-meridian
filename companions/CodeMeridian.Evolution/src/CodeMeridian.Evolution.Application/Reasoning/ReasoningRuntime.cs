using CodeMeridian.Evolution.Application.Ledger;
using CodeMeridian.Evolution.Application.Observations;

namespace CodeMeridian.Evolution.Application.Reasoning;

public sealed class ReasoningRuntime(
    IEnumerable<IReasoningProvider> providers,
    CognitiveLedgerService ledgerService,
    TimeProvider timeProvider)
{
    private readonly IReadOnlyDictionary<string, IReasoningProvider> providersById = providers
        .ToDictionary(provider => provider.Id, StringComparer.Ordinal);

    public async Task<IReadOnlyList<ProviderCapabilities>> ProbeAllAsync(
        CancellationToken cancellationToken = default)
    {
        var results = new List<ProviderCapabilities>(providersById.Count);

        foreach (var provider in providersById.Values.OrderBy(
                     provider => provider.Id,
                     StringComparer.Ordinal))
        {
            results.Add(await provider.ProbeAsync(cancellationToken).ConfigureAwait(false));
        }

        return results;
    }

    public async Task<ReasoningResult> InvokeAsync(
        ReasoningRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ProviderId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Role);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Goal);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ProjectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.IdempotencyKey);

        if (request.MaximumOutputTokens is < 1 or > 32_768)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                request.MaximumOutputTokens,
                "Maximum output tokens must be between 1 and 32768.");
        }

        if (request.Timeout <= TimeSpan.Zero || request.Timeout > TimeSpan.FromMinutes(10))
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                request.Timeout,
                "Reasoning timeout must be positive and no more than ten minutes.");
        }

        var snapshot = await ledgerService
            .GetSnapshotAsync(cancellationToken)
            .ConfigureAwait(false);

        if (snapshot.IsPaused)
        {
            throw new InvalidOperationException(
                "Reasoning is blocked while the governance kernel is paused.");
        }

        if (!providersById.TryGetValue(request.ProviderId, out var provider))
        {
            throw new KeyNotFoundException(
                $"Reasoning provider '{request.ProviderId}' is not registered.");
        }

        var capabilities = await provider
            .ProbeAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!capabilities.IsAvailable ||
            !capabilities.Roles.Contains(request.Role, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Provider '{provider.Id}' cannot serve role '{request.Role}'.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(request.Timeout);
        var result = await provider
            .InvokeAsync(request, timeout.Token)
            .ConfigureAwait(false);
        var observedAt = timeProvider.GetUtcNow();

        await ledgerService.RecordObservationAsync(
            new ObservationRequest(
                $"reasoning:{request.InvocationId:D}",
                $"reasoning-provider:{provider.Id}",
                "reasoning-result",
                result.Summary,
                result.Abstained ? "warning" : "information",
                observedAt,
                1m - result.Uncertainty,
                request.IdempotencyKey)
            {
                ProjectId = request.ProjectId,
                TrustLevel = "model-generated"
            },
            cancellationToken).ConfigureAwait(false);

        return result;
    }

    public Task CancelAsync(
        string providerId,
        Guid invocationId,
        CancellationToken cancellationToken = default)
    {
        return providersById.TryGetValue(providerId, out var provider)
            ? provider.CancelAsync(invocationId, cancellationToken)
            : throw new KeyNotFoundException(
                $"Reasoning provider '{providerId}' is not registered.");
    }
}
