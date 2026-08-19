using System.Data;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AecoPostMortem.Data;

/// <summary>
/// The system-prompt dedup append path (FR-12): multi-row <c>INSERT</c> statements issued straight
/// at the connection, outside EF Core's change tracker — the same door <see cref="RawEventBatch"/>
/// uses for RAW, for the same reason (Repo Rule 5): rows here can run to tens of thousands of
/// characters each, and per-entity tracking is the wrong shape for a batch this large.
/// </summary>
public static class SystemPromptTextBatch
{
    /// <summary>Rows per statement. Lower than <see cref="RawEventBatch.DefaultRowsPerStatement"/>
    /// because each row here can be a measured 54,335 characters (data map Part 6) rather than one
    /// RAW envelope, so the same row count binds far more text per statement.</summary>
    public const int DefaultRowsPerStatement = 64;

    static readonly SqliteType[] ColumnTypes =
    [
        SqliteType.Text, // content_hash
        SqliteType.Text, // text
    ];

    /// <summary>
    /// Append prompt texts, skipping any whose content hash is already stored. That skip is what
    /// makes storing the same prompt from many sessions collapse to one row (FR-12) instead of
    /// depending on the caller to have deduplicated first.
    /// </summary>
    /// <returns>The number of rows actually inserted — appended minus already present.</returns>
    public static int Append(
        PostMortemContext context,
        IEnumerable<SystemPromptText> texts,
        int rowsPerStatement = DefaultRowsPerStatement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(texts);
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
            var batch = new List<SystemPromptText>(rowsPerStatement);
            SqliteCommand? full = null;

            try
            {
                foreach (var text in texts)
                {
                    batch.Add(text);
                    if (batch.Count < rowsPerStatement)
                    {
                        continue;
                    }

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

    static int Execute(SqliteCommand command, List<SystemPromptText> batch)
    {
        for (var row = 0; row < batch.Count; row++)
        {
            var text = batch[row];
            command.Parameters[Name(row, 0)].Value = text.ContentHash;
            command.Parameters[Name(row, 1)].Value = text.Text;
        }

        return command.ExecuteNonQuery();
    }

    /// <summary>
    /// <c>ON CONFLICT … DO NOTHING</c> against the content-hash primary key, naming the conflict
    /// target the same way <see cref="RawEventBatch"/> does, so a genuine constraint failure still
    /// throws instead of being swallowed as one more duplicate.
    /// </summary>
    static string Sql(int rows)
    {
        var columns = SystemPromptTextSchema.WrittenColumns;
        var sql = new StringBuilder("INSERT INTO ")
            .Append(SystemPromptTextSchema.Table)
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
            .Append(SystemPromptTextSchema.ContentHash)
            .Append(") DO NOTHING")
            .ToString();
    }

    static string Name(int row, int column) => $"$r{row}c{column}";
}
