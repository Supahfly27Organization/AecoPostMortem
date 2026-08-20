using System.Data;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AecoPostMortem.Data;

/// <summary>
/// The RAW append path: multi-row <c>INSERT</c> statements issued straight at the connection,
/// deliberately outside EF Core's change tracker (PRD §3.1). A measured 56,138 rows arrive in one
/// full ingest and per-entity tracking is the wrong shape for that — the same schema, a different
/// door. Everything else in the store goes through the <see cref="PostMortemContext"/>.
/// </summary>
public static class RawEventBatch
{
    /// <summary>
    /// Rows per statement. SQLite's parameter ceiling is what bounds this — nine columns a row, so
    /// this batch binds 2,304 of a default 32,766 — and beyond a few hundred rows the win flattens
    /// while the statement cache stops being reusable.
    /// </summary>
    public const int DefaultRowsPerStatement = 256;

    /// <summary>The bound type of each column in <see cref="RawEventSchema.WrittenColumns"/> order.
    /// Stated rather than inferred: binding a sequence number as text would sort it lexically and
    /// make <c>ix_raw_session_seq</c> order the tape wrongly.</summary>
    static readonly SqliteType[] ColumnTypes =
    [
        SqliteType.Text,     // session_id
        SqliteType.Integer,  // seq
        SqliteType.Text,     // event_type
        SqliteType.Text,     // ts
        SqliteType.Text,     // provider_version
        SqliteType.Text,     // source_file
        SqliteType.Integer,  // byte_offset
        SqliteType.Text,     // content_hash
        SqliteType.Text,     // payload
    ];

    /// <summary>
    /// Append events, skipping any whose FR-2 identity is already stored. That skip is what makes
    /// re-running ingestion over the same logs add nothing (FR-5): idempotency is a property of the
    /// unique index, not of the caller remembering where it stopped.
    /// </summary>
    /// <returns>The number of rows actually inserted — appended minus already present.</returns>
    public static int Append(
        PostMortemContext context,
        IEnumerable<RawEvent> events,
        int rowsPerStatement = DefaultRowsPerStatement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentOutOfRangeException.ThrowIfLessThan(rowsPerStatement, 1);

        var connection = (SqliteConnection)context.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            connection.Open();
        }

        try
        {
            using var transaction = connection.BeginTransaction();

            var inserted = 0;
            var batch = new List<RawEvent>(rowsPerStatement);
            SqliteCommand? full = null;

            try
            {
                foreach (var raw in events)
                {
                    batch.Add(raw);
                    if (batch.Count < rowsPerStatement)
                    {
                        continue;
                    }

                    // One command, rebound per batch: the statement text is identical for every
                    // full batch, so SQLite prepares it once however many batches follow.
                    full ??= CreateCommand(connection, transaction, rowsPerStatement);
                    inserted += Execute(full, batch);
                    batch.Clear();
                }

                if (batch.Count > 0)
                {
                    using var tail = CreateCommand(connection, transaction, batch.Count);
                    inserted += Execute(tail, batch);
                }
            }
            finally
            {
                full?.Dispose();
            }

            transaction.Commit();
            return inserted;
        }
        finally
        {
            if (openedHere)
            {
                connection.Close();
            }
        }
    }

    static SqliteCommand CreateCommand(SqliteConnection connection, SqliteTransaction transaction, int rows)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = Sql(rows);

        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < ColumnTypes.Length; column++)
            {
                command.Parameters.Add(new SqliteParameter(Name(row, column), ColumnTypes[column]));
            }
        }

        return command;
    }

    static int Execute(SqliteCommand command, List<RawEvent> batch)
    {
        for (var row = 0; row < batch.Count; row++)
        {
            var raw = batch[row];
            command.Parameters[Name(row, 0)].Value = raw.SessionId;
            command.Parameters[Name(row, 1)].Value = raw.Sequence;
            command.Parameters[Name(row, 2)].Value = raw.EventType;
            command.Parameters[Name(row, 3)].Value = raw.Timestamp;
            command.Parameters[Name(row, 4)].Value = raw.ProviderVersion;
            command.Parameters[Name(row, 5)].Value = raw.SourceFile;
            command.Parameters[Name(row, 6)].Value = raw.ByteOffset;
            command.Parameters[Name(row, 7)].Value = raw.ContentHash;
            command.Parameters[Name(row, 8)].Value = raw.Payload;
        }

        return command.ExecuteNonQuery();
    }

    /// <summary>
    /// <c>ON CONFLICT … DO NOTHING</c> against the identity index rather than <c>INSERT OR
    /// IGNORE</c>: naming the conflict target means a duplicate line is skipped while a genuine
    /// constraint failure — a NULL where the schema requires a value — still throws instead of
    /// being swallowed as one more duplicate.
    /// </summary>
    static string Sql(int rows)
    {
        var columns = RawEventSchema.WrittenColumns;
        var sql = new StringBuilder("INSERT INTO ")
            .Append(RawEventSchema.Table)
            .Append(" (")
            .AppendJoin(", ", columns)
            .Append(") VALUES ");

        for (var row = 0; row < rows; row++)
        {
            sql.Append(row == 0 ? "(" : ", (");
            for (var column = 0; column < columns.Count; column++)
            {
                sql.Append(column == 0 ? string.Empty : ", ").Append(Name(row, column));
            }

            sql.Append(')');
        }

        return sql
            .Append(" ON CONFLICT (")
            .Append(RawEventSchema.SourceFile).Append(", ")
            .Append(RawEventSchema.ByteOffset).Append(", ")
            .Append(RawEventSchema.ContentHash)
            .Append(") DO NOTHING")
            .ToString();
    }

    static string Name(int row, int column) => $"$r{row}c{column}";

    /// <summary>
    /// Read-only: finds events whose <c>(source_file, byte_offset)</c> already carries a stored
    /// row with a <em>different</em> content hash — the signature of a file rewritten rather than
    /// appended to (FR-5's edge case: byte offsets are safe identity only because growth is
    /// append-only). Nothing is written or deleted here; the caller decides whether it is still
    /// safe to <see cref="Append"/>. A matching content hash at the same offset is not a
    /// mismatch — that is the ordinary re-ingest case <see cref="Append"/> already treats as a
    /// no-op via the identity index.
    /// </summary>
    public static IReadOnlyList<RawRewriteMismatch> DetectRewrites(PostMortemContext context, IEnumerable<RawEvent> events)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(events);

        var batch = events as IReadOnlyCollection<RawEvent> ?? events.ToArray();
        if (batch.Count == 0)
        {
            return [];
        }

        var sourceFiles = batch.Select(raw => raw.SourceFile).Distinct(StringComparer.Ordinal).ToArray();

        // OrderBy(Id) before the grouping so a duplicate (source_file, byte_offset) pair — only
        // reachable via data written before this check existed — resolves to the most recently
        // inserted hash rather than to whichever row an unordered GroupBy happens to see first.
        var stored = context.RawEvents
            .Where(row => sourceFiles.Contains(row.SourceFile))
            .OrderBy(row => row.Id)
            .Select(row => new { row.SourceFile, row.ByteOffset, row.ContentHash })
            .ToList()
            .GroupBy(row => (row.SourceFile, row.ByteOffset))
            .ToDictionary(group => group.Key, group => group.Last().ContentHash);

        var mismatches = new List<RawRewriteMismatch>();
        foreach (var raw in batch)
        {
            if (stored.TryGetValue((raw.SourceFile, raw.ByteOffset), out var storedHash)
                && !string.Equals(storedHash, raw.ContentHash, StringComparison.Ordinal))
            {
                mismatches.Add(new RawRewriteMismatch(raw.SourceFile, raw.ByteOffset, storedHash, raw.ContentHash));
            }
        }

        return mismatches;
    }

    /// <summary>
    /// FR-7's retroactive case: a session already ingested before its cwd was added to the
    /// exclusion list has to be removable, not just refused on the next read. Issued as a bulk
    /// <c>DELETE</c> via EF Core's <c>ExecuteDelete</c> rather than loaded-then-tracked-then-removed
    /// — the same "not per-entity tracking" reasoning as <see cref="Append"/>, just for the opposite
    /// direction. Deleting a session with nothing stored is not an error; it returns 0.
    /// </summary>
    public static int DeleteBySession(PostMortemContext context, string sessionId)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        return context.RawEvents.Where(row => row.SessionId == sessionId).ExecuteDelete();
    }
}

/// <summary>
/// One event whose byte offset was already stored under a different content hash — a rewritten
/// file caught before <see cref="RawEventBatch.Append"/> could merge two different histories under
/// one <see cref="SourceFile"/> (FR-5).
/// </summary>
public sealed record RawRewriteMismatch(string SourceFile, long ByteOffset, string StoredContentHash, string ReadContentHash);
