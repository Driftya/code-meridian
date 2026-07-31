using CodeMeridian.Evolution.Application.Journal;
using CodeMeridian.Evolution.Application.Reasoning;
using CodeMeridian.Evolution.Domain.Cognition;

namespace CodeMeridian.Evolution.Application.Cognition;

public sealed record CognitiveCycleResult(
    Guid CycleId,
    CognitiveCycleStatus Status,
    string ProjectId,
    AttentionFrame? Attention,
    ReasoningResult? Reasoning,
    MentalSimulation? Simulation,
    AffectState Affect,
    IReadOnlyList<DriveState> Drives,
    JournalAppendResult? Journal);

