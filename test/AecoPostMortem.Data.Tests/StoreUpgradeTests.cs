using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AecoPostMortem.Data.Tests;

/// <summary>
/// A store written by an earlier build is brought forward, not rejected (FR-11). RAW is the only
/// layer this applies to: it holds provider events that no longer exist at the source once
/// Copilot's window rotates, so its schema moves under migrations while the derived layers are
/// dropped and re-derived (PRD §3.8).
/// </summary>
public sealed class StoreUpgradeTests
{
    [Fact]
    public void Opening_a_store_again_keeps_the_rows_already_in_it()
    {
        using var temporary = new TemporaryStore();
        const string payload = """{"type":"session.start","data":{"copilotVersion":"0.0.339"}}""";

        using (var first = temporary.Store.Open())
        {
            RawEventBatch.Append(first, [Events.From(payload)]);
        }

        using var second = temporary.Store.Open();

        Assert.Equal(payload, second.RawEvents.Single().Payload);
        Assert.Empty(second.Database.GetPendingMigrations());
    }

    /// <summary>
    /// The store is rolled back to the migration before the current one and then simply opened; the
    /// open is what has to bring it forward, with no command from the operator.
    ///
    /// The target is derived from the migration list rather than named, so this covers the real
    /// case the moment a second migration lands. With one migration the earlier state is the empty
    /// database, which is exactly what an older build with no RAW schema at all looks like — so the
    /// seeded half is skipped, and the upgrade half still runs.
    /// </summary>
    [Fact]
    public void A_store_at_an_earlier_migration_is_brought_forward_with_its_rows_intact()
    {
        using var temporary = new TemporaryStore();
        Directory.CreateDirectory(temporary.Folder);

        string[] defined;
        using (var built = new PostMortemContext(temporary.Store.Options()))
        {
            defined = built.Database.GetMigrations().ToArray();
            // "0" is EF Core's name for the state before the first migration.
            var earlier = defined.Length > 1 ? defined[^2] : "0";
            built.GetService<IMigrator>().Migrate(earlier);
        }

        var seeded = SeedThroughRawSql(temporary.Store);

        using var opened = temporary.Store.Open();

        Assert.Equal(defined, opened.Database.GetAppliedMigrations());
        Assert.Empty(opened.Database.GetPendingMigrations());

        if (seeded is not null)
        {
            Assert.Equal(seeded, opened.RawEvents.Single().Payload);
        }
    }

    /// <summary>
    /// Writes one row with whatever columns the earlier schema actually has, discovered from the
    /// database rather than from the current model — the point of the exercise is that the row
    /// predates the model. Returns null when RAW does not exist yet at that migration.
    /// </summary>
    static string? SeedThroughRawSql(LocalStore store)
    {
        using var connection = new SqliteConnection(store.ConnectionString);
        connection.Open();

        using var columns = connection.CreateCommand();
        columns.CommandText = "SELECT name FROM pragma_table_info('raw_event') WHERE name <> 'id'";

        var names = new List<string>();
        using (var reader = columns.ExecuteReader())
        {
            while (reader.Read())
            {
                names.Add(reader.GetString(0));
            }
        }

        if (names.Count == 0)
        {
            return null;
        }

        const string payload = """{"type":"session.start","data":{"writtenBy":"an earlier build"}}""";

        using var insert = connection.CreateCommand();
        insert.CommandText =
            $"INSERT INTO raw_event ({string.Join(", ", names)}) "
            + $"VALUES ({string.Join(", ", names.Select(name => "$" + name))})";

        foreach (var name in names)
        {
            insert.Parameters.AddWithValue("$" + name, ValueFor(name, payload));
        }

        insert.ExecuteNonQuery();
        return payload;
    }

    static object ValueFor(string column, string payload) => column switch
    {
        "seq" or "byte_offset" => 0L,
        "payload" => payload,
        "content_hash" => RawPayload.ContentHashOfText(payload),
        _ => "an earlier build",
    };
}
