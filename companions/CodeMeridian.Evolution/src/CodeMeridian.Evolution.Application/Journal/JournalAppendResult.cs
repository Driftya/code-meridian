using CodeMeridian.Evolution.Domain.Ledger;

namespace CodeMeridian.Evolution.Application.Journal;

public sealed record JournalAppendResult(
    JournalEntry Entry,
    bool WasAppended);
