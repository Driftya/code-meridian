using System.Text.Json;
using System.Text.Json.Serialization;
using CodeMeridian.Evolution.Domain.Ledger;
using Npgsql;
using NpgsqlTypes;

namespace CodeMeridian.Evolution.Infrastructure.Journal;

public sealed partial class PostgreSqlJournalStore
{
    private static async Task InsertEntryAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        JournalEntry entry,
        CancellationToken cancellationToken)
    {
        await InsertJournalEventAsync(
            connection,
            transaction,
            entry,
            cancellationToken).ConfigureAwait(false);
        await InsertTransactionAsync(
            connection,
            transaction,
            entry,
            cancellationToken).ConfigureAwait(false);

        for (var index = 0; index < entry.Transaction.Postings.Count; index++)
        {
            await InsertPostingAsync(
                connection,
                transaction,
                entry.Sequence,
                index,
                entry.Transaction.Postings[index],
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task InsertJournalEventAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        JournalEntry entry,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO journal_events (
                sequence,
                appended_at,
                previous_hash,
                hash,
                transaction_id,
                idempotency_key)
            VALUES (
                @sequence,
                @appended_at,
                @previous_hash,
                @hash,
                @transaction_id,
                @idempotency_key);
            """;
        command.Parameters.AddWithValue("sequence", entry.Sequence);
        command.Parameters.AddWithValue("appended_at", entry.AppendedAt);
        command.Parameters.AddWithValue("previous_hash", entry.PreviousHash);
        command.Parameters.AddWithValue("hash", entry.Hash);
        command.Parameters.AddWithValue("transaction_id", entry.Transaction.Id);
        command.Parameters.AddWithValue(
            "idempotency_key",
            (object?)entry.Transaction.IdempotencyKey ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertTransactionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        JournalEntry entry,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO ledger_transactions (
                transaction_id,
                event_sequence,
                transaction_json)
            VALUES (
                @transaction_id,
                @event_sequence,
                @transaction_json);
            """;
        command.Parameters.AddWithValue("transaction_id", entry.Transaction.Id);
        command.Parameters.AddWithValue("event_sequence", entry.Sequence);
        command.Parameters.AddWithValue(
            "transaction_json",
            NpgsqlDbType.Jsonb,
            JsonSerializer.Serialize(entry.Transaction, JsonOptions));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertPostingAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long eventSequence,
        int postingIndex,
        LedgerPosting posting,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO ledger_postings (
                event_sequence,
                posting_index,
                account,
                subject_id,
                summary,
                provenance,
                confidence,
                reconciliation)
            VALUES (
                @event_sequence,
                @posting_index,
                @account,
                @subject_id,
                @summary,
                @provenance,
                @confidence,
                @reconciliation);
            """;
        command.Parameters.AddWithValue("event_sequence", eventSequence);
        command.Parameters.AddWithValue("posting_index", postingIndex);
        command.Parameters.AddWithValue("account", posting.Account.ToString());
        command.Parameters.AddWithValue("subject_id", posting.SubjectId);
        command.Parameters.AddWithValue("summary", posting.Summary);
        command.Parameters.AddWithValue("provenance", posting.Provenance);
        command.Parameters.AddWithValue("confidence", posting.Confidence);
        command.Parameters.AddWithValue(
            "reconciliation",
            posting.Reconciliation.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static JournalEntry ReadEntry(NpgsqlDataReader reader)
    {
        var transaction = JsonSerializer.Deserialize<CognitiveTransaction>(
            reader.GetString(4),
            JsonOptions)
            ?? throw new InvalidOperationException("Stored cognitive transaction is invalid.");
        var appendedAt = new DateTimeOffset(
            DateTime.SpecifyKind(reader.GetDateTime(1), DateTimeKind.Utc));
        return new JournalEntry(
            reader.GetInt64(0),
            appendedAt,
            reader.GetString(2),
            reader.GetString(3),
            transaction);
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}

