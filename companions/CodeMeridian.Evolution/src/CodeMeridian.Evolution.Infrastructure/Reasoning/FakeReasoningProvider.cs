using System.Collections.Concurrent;
using CodeMeridian.Evolution.Application.Reasoning;

namespace CodeMeridian.Evolution.Infrastructure.Reasoning;

public sealed class FakeReasoningProvider : IReasoningProvider
{
    private static readonly IReadOnlyList<string> SupportedRoles =
        Array.AsReadOnly(["planner", "researcher", "critic", "verifier", "summarizer"]);
    private readonly ConcurrentDictionary<Guid, byte> cancelledInvocations = new();

    public string Id => "fake";

    public Task<ProviderCapabilities> ProbeAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new ProviderCapabilities(
            Id,
            "1.0.0",
            IsAvailable: true,
            SupportsStructuredOutput: true,
            SupportsCancellation: true,
            SupportsContinuation: false,
            IsReadOnly: true,
            SupportedRoles));
    }

    public Task<ReasoningResult> InvokeAsync(
        ReasoningRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (cancelledInvocations.ContainsKey(request.InvocationId))
        {
            throw new OperationCanceledException(
                $"Invocation '{request.InvocationId}' was cancelled.",
                cancellationToken);
        }

        var summary = request.EvidenceIds.Count == 0
            ? $"Abstained from '{request.Goal}' because no evidence was supplied."
            : $"Deterministic {request.Role} assessment for '{request.Goal}' " +
              $"using {request.EvidenceIds.Count} evidence reference(s).";

        return Task.FromResult(new ReasoningResult(
            request.InvocationId,
            Id,
            summary,
            request.EvidenceIds.ToArray(),
            ["Collect more evidence.", "Maintain the current hypothesis."],
            request.EvidenceIds.Count == 0 ? 1m : 0.25m,
            Abstained: request.EvidenceIds.Count == 0,
            ContinuationToken: null));
    }

    public Task CancelAsync(
        Guid invocationId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        cancelledInvocations.TryAdd(invocationId, 0);
        return Task.CompletedTask;
    }
}
