using CodeMeridian.Evolution.Application.Projections;

namespace CodeMeridian.Evolution.Application.Cognition;

public sealed record AttentionSelection(
    LedgerItemView Item,
    decimal Score,
    string Reason);

