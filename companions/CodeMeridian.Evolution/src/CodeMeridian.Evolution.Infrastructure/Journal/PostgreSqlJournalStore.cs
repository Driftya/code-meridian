using System.Data;
using System.Text.Json;
using CodeMeridian.Evolution.Application.Journal;
using CodeMeridian.Evolution.Domain.Ledger;
using Npgsql;

namespace CodeMeridian.Evolution.Infrastructure.Journal;

public sealed partial class PostgreSqlJournalStore(NpgsqlDataSource dataSource) :
    IJournalStore,
    IDisposable
{
    private const long JournalLockId = 6_277_331_904_823_117_641L;
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly SemaphoreSlim initializationLock = new(1, 1);
    private volatile bool initialized;

    public async Task<JournalAppendResult> AppendAsync(
        CognitiveTransaction transaction,
        DateTimeOffset appendedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        transaction.Validate();
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = await dataSource
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var databaseTransaction = await connection
            .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            .ConfigureAwait(false);

        await AcquireJournalLockAsync(connection, databaseTransaction, cancellationToken)
            .ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(transaction.IdempotencyKey))
        {
            var existing = await FindByIdempotencyKeyAsync(
                connection,
                databaseTransaction,
                transaction.IdempotencyKey,
                cancellationToken).ConfigureAwait(false);

            if (existing is not null)
            {
                await databaseTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return new JournalAppendResult(existing, WasAppended: false);
            }
        }

        var head = await ReadHeadAsync(
            connection,
            databaseTransaction,
            cancellationToken).ConfigureAwait(false);
        var entry = JournalEntry.Create(
            head.Sequence + 1,
            appendedAt,
            head.Hash,
            transaction);

        await InsertEntryAsync(
            connection,
            databaseTransaction,
            entry,
            cancellationToken).ConfigureAwait(false);
        await databaseTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new JournalAppendResult(entry, WasAppended: true);
    }

    public async Task<IReadOnlyList<JournalEntry>> ReadAllAsync(
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
        await using var connection = await dataSource
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT e.sequence,
                   e.appended_at,
                   e.previous_hash,
                   e.hash,
                   t.transaction_json::text
              FROM journal_events e
              JOIN ledger_transactions t ON t.transaction_id = e.transaction_id
             ORDER BY e.sequence;
            """;
        await using var reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        var entries = new List<JournalEntry>();

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            entries.Add(ReadEntry(reader));
        }

        return Array.AsReadOnly(entries.ToArray());
    }

    public void Dispose()
    {
        initializationLock.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (initialized)
        {
            return;
        }

        await initializationLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (initialized)
            {
                return;
            }

            await using var command = dataSource.CreateCommand(PostgreSqlJournalSchema.Sql);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            initialized = true;
        }
        finally
        {
            initializationLock.Release();
        }
    }

    private static async Task AcquireJournalLockAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT pg_advisory_xact_lock(@lock_id);";
        command.Parameters.AddWithValue("lock_id", JournalLockId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<JournalEntry?> FindByIdempotencyKeyAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT e.sequence,
                   e.appended_at,
                   e.previous_hash,
                   e.hash,
                   t.transaction_json::text
              FROM journal_events e
              JOIN ledger_transactions t ON t.transaction_id = e.transaction_id
             WHERE e.idempotency_key = @idempotency_key;
            """;
        command.Parameters.AddWithValue("idempotency_key", idempotencyKey);
        await using var reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? ReadEntry(reader)
            : null;
    }

    private static async Task<(long Sequence, string Hash)> ReadHeadAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT sequence, hash
              FROM journal_events
             ORDER BY sequence DESC
             LIMIT 1;
            """;
        await using var reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? (reader.GetInt64(0), reader.GetString(1))
            : (0L, string.Empty);
    }

}
