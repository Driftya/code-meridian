namespace CodeMeridian.Evolution.Application.Reasoning;

public sealed record ProviderCapabilities(
    string ProviderId,
    string AdapterVersion,
    bool IsAvailable,
    bool SupportsStructuredOutput,
    bool SupportsCancellation,
    bool SupportsContinuation,
    bool IsReadOnly,
    IReadOnlyList<string> Roles);
