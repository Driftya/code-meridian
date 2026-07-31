namespace CodeMeridian.Evolution.Infrastructure.Journal;

internal static class PostgreSqlJournalSchema
{
    public const string Sql = """
        CREATE TABLE IF NOT EXISTS journal_events (
            sequence BIGINT PRIMARY KEY,
            appended_at TIMESTAMPTZ NOT NULL,
            previous_hash TEXT NOT NULL,
            hash TEXT NOT NULL,
            transaction_id UUID NOT NULL UNIQUE,
            idempotency_key TEXT NULL UNIQUE
        );

        CREATE TABLE IF NOT EXISTS ledger_transactions (
            transaction_id UUID PRIMARY KEY,
            event_sequence BIGINT NOT NULL UNIQUE REFERENCES journal_events(sequence),
            transaction_json JSONB NOT NULL
        );

        CREATE TABLE IF NOT EXISTS ledger_postings (
            event_sequence BIGINT NOT NULL REFERENCES journal_events(sequence),
            posting_index INTEGER NOT NULL,
            account TEXT NOT NULL,
            subject_id TEXT NOT NULL,
            summary TEXT NOT NULL,
            provenance TEXT NOT NULL,
            confidence NUMERIC NOT NULL,
            reconciliation TEXT NOT NULL,
            PRIMARY KEY (event_sequence, posting_index)
        );

        CREATE INDEX IF NOT EXISTS ix_ledger_postings_account_subject
            ON ledger_postings(account, subject_id, event_sequence DESC);
        """;
}
