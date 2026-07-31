using CodeMeridian.Evolution.Domain.Ledger;

namespace CodeMeridian.Evolution.Application.Projections;

public sealed record LedgerAccountView(
    LedgerAccount Account,
    IReadOnlyList<LedgerItemView> Items);
