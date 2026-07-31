using CodeMeridian.Evolution.Domain.Cognition;
using CodeMeridian.Evolution.Domain.Governance;
using CodeMeridian.Evolution.Domain.Ledger;

namespace CodeMeridian.Evolution.Application.Projections;

public sealed record CognitiveSnapshot(
    DateTimeOffset GeneratedAt,
    bool IsPaused,
    AutonomyLevel AutonomyLevel,
    long EntryCount,
    string HeadHash,
    bool IsBalanced,
    IReadOnlyList<TrialBalanceViolation> IntegrityViolations,
    IReadOnlyList<LedgerAccountView> Accounts,
    IReadOnlyList<LedgerItemView> ActiveGoals,
    IReadOnlyList<LedgerItemView> Attention,
    IReadOnlyList<LedgerItemView> Unresolved,
    AffectState Affect,
    IReadOnlyList<DriveState> Drives);
