using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AecoPostMortem.Data.Tests;

/// <summary>
/// The created schema, inspected. The indexes are asserted rather than left to a later profiling
/// pass because they were measured to be load-bearing: without the covering indexes the per-tool
/// aggregate ran 776.06 ms against 56.15 ms on Postgres — a measured 13.8× — and 64.34 ms with them
/// (<c>docs/product-superpowers/research/2026-08-16-sqlite-vs-postgres-query-latency.md</c> Part 3).
/// A missing index fails here instead of degrading a surface quietly.
/// </summary>
public sealed class SchemaTests
{
    /// <summary>
    /// Named literally rather than read back from <see cref="RawEventSchema"/>: a test that took
    /// its expectations from the code it checks would pass an index being renamed out of existence.
    /// </summary>
    public static TheoryData<string, bool> RequiredIndexes() => new()
    {
        { "ux_raw_identity", true },        // FR-2's identity triple, and FR-5's idempotency
        { "ix_raw_session_seq", false },    // the Flight Recorder's tape, in order
        { "ix_raw_type", false },           // the event census, counted by type
    };

    [Theory]
    [MemberData(nameof(RequiredIndexes))]
    public void The_read_paths_index_exists(string name, bool unique)
    {
        using var temporary = new TemporaryStore();
        using var context = temporary.Store.Open();

        var indexes = IndexesOn(context, "raw_event");

        Assert.True(
            indexes.ContainsKey(name),
            $"raw_event has no index '{name}'. The measured query shapes depend on it; adding it "
            + "back is the fix, not relaxing this test.");
        Assert.Equal(unique, indexes[name]);
    }

    [Fact]
    public void The_migrations_create_only_RAW_and_the_stores_own_metadata()
    {
        // Repo Rule 4 / PRD §3.8: NORMALIZED and FINDINGS are re-derived from RAW, never migrated.
        // A third table appearing here means a migration was authored against a derived layer.
        //
        // store_metadata is migrated deliberately and is not a derived layer: it records the
        // store's own state, including the derived schema's version, and a value dropped alongside
        // the tables it describes could not be compared against them.
        using var temporary = new TemporaryStore();
        using var context = temporary.Store.Open();

        var derived = context.Model.GetEntityTypes()
            .Where(type => typeof(AecoPostMortem.Data.Execution.IDerivedEntity).IsAssignableFrom(type.ClrType))
            .Select(type => type.GetTableName()!)
            .ToHashSet(StringComparer.Ordinal);

        // sqlite_% and __EF% are the engine's and EF Core's own bookkeeping, not the product's model.
        var tables = Query(
                context,
                "SELECT name FROM sqlite_master WHERE type = 'table' "
                + "AND name NOT LIKE 'sqlite_%' AND name NOT LIKE '\\_\\_EF%' ESCAPE '\\' "
                + "ORDER BY name")
            .Where(name => !derived.Contains(name))
            .ToArray();

        Assert.Equal(["raw_event", "store_metadata"], tables);
    }

    [Fact]
    public void The_identity_triple_is_what_the_unique_index_covers()
    {
        using var temporary = new TemporaryStore();
        using var context = temporary.Store.Open();

        var columns = Query(context, "SELECT name FROM pragma_index_info('ux_raw_identity')").ToArray();

        Assert.Equal(["source_file", "byte_offset", "content_hash"], columns);
    }

    static Dictionary<string, bool> IndexesOn(PostMortemContext context, string table)
    {
        var connection = (SqliteConnection)context.Database.GetDbConnection();
        connection.Open();

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT name, \"unique\" FROM pragma_index_list($table)";
            command.Parameters.AddWithValue("$table", table);

            using var reader = command.ExecuteReader();
            var indexes = new Dictionary<string, bool>(StringComparer.Ordinal);
            while (reader.Read())
            {
                indexes[reader.GetString(0)] = reader.GetBoolean(1);
            }

            return indexes;
        }
        finally
        {
            connection.Close();
        }
    }

    static IEnumerable<string> Query(PostMortemContext context, string sql)
    {
        var connection = (SqliteConnection)context.Database.GetDbConnection();
        connection.Open();

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;

            using var reader = command.ExecuteReader();
            var values = new List<string>();
            while (reader.Read())
            {
                values.Add(reader.GetString(0));
            }

            return values;
        }
        finally
        {
            connection.Close();
        }
    }
}
