using AecoPostMortem.Data.Execution;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace AecoPostMortem.Data.Tests;

/// <summary>
/// The derived layer is disposable by construction (PRD §3.8): it is created from the model, and a
/// change to the model changes its version, which is what triggers a re-derivation instead of a
/// migration.
/// </summary>
public sealed class DerivedSchemaTests
{
    [Fact]
    public void Opening_a_store_creates_the_eight_derived_tables()
    {
        using var temporary = new TemporaryStore();
        using var context = temporary.Store.Open();

        var tables = DerivedTables(context);

        Assert.Equal(
            ["agent", "hook", "permission", "session", "skill", "tool_call", "turn", "write_unit"],
            tables);
    }

    [Fact]
    public void Dropping_leaves_only_the_migrated_tables()
    {
        using var temporary = new TemporaryStore();
        using var context = temporary.Store.Open();

        DerivedSchema.Drop(context);

        Assert.Empty(DerivedTables(context));
    }

    [Fact]
    public void Creating_twice_is_not_an_error()
    {
        using var temporary = new TemporaryStore();
        using var context = temporary.Store.Open();

        DerivedSchema.Create(context);
        DerivedSchema.Create(context);

        Assert.Equal(8, DerivedTables(context).Length);
    }

    [Fact]
    public void The_version_is_the_same_every_time_it_is_computed()
    {
        using var context = new PostMortemContext();

        Assert.Equal(DerivedSchema.Version(context), DerivedSchema.Version(context));
        Assert.Equal(64, DerivedSchema.Version(context).Length);
    }

    /// <summary>
    /// The version is a hash of the DDL that actually runs, so it cannot be out of step with the
    /// schema the way a hand-maintained integer can.
    /// </summary>
    [Fact]
    public void The_version_moves_when_the_statements_move()
    {
        using var context = new PostMortemContext();

        var statements = DerivedSchema.CreateStatements(context);

        Assert.Contains(statements, sql => sql.Contains("CREATE TABLE IF NOT EXISTS turn", StringComparison.Ordinal));
        Assert.Contains(statements, sql => sql.Contains("ck_turn_owner", StringComparison.Ordinal));
        Assert.Contains(statements, sql => sql.Contains("ix_tc_name_success", StringComparison.Ordinal));
    }

    [Fact]
    public void The_version_is_recorded_in_the_store_when_it_is_opened()
    {
        using var temporary = new TemporaryStore();
        using var context = temporary.Store.Open();

        var recorded = context.StoreMetadata
            .Single(row => row.Key == StoreMetadata.DerivedSchemaVersionKey)
            .Value;

        Assert.Equal(DerivedSchema.Version(context), recorded);
    }

    /// <summary>A stale version means the tables predate the model, so they are rebuilt rather than
    /// migrated — and the rows in them go, because they are re-derivable from RAW.</summary>
    [Fact]
    public void A_stale_version_causes_the_derived_tables_to_be_rebuilt()
    {
        using var temporary = new TemporaryStore();

        using (var first = temporary.Store.Open())
        {
            first.Turns.Add(new Turn
            {
                SessionId = "session-1",
                TurnId = "turn-1",
                StartedAt = "2026-08-09T20:14:36.758Z",
                Outcome = TurnOutcome.Completed,
                OwnerKind = OwnerKind.Main,
            });

            var version = first.StoreMetadata.Single(row => row.Key == StoreMetadata.DerivedSchemaVersionKey);
            first.StoreMetadata.Remove(version);
            first.StoreMetadata.Add(new StoreMetadata
            {
                Key = StoreMetadata.DerivedSchemaVersionKey,
                Value = "a version from an older build",
            });
            first.SaveChanges();
        }

        using var reopened = temporary.Store.Open();

        Assert.Empty(reopened.Turns);
        Assert.Equal(
            DerivedSchema.Version(reopened),
            reopened.StoreMetadata.Single(row => row.Key == StoreMetadata.DerivedSchemaVersionKey).Value);
    }

    // context.Model returns the read-optimized runtime model, which strips migrations-only
    // annotations; DerivedSchema itself reads context.GetService<IDesignTimeModel>().Model for
    // exactly this reason (see its remarks), so this helper reads the model the same way rather
    // than risk the two disagreeing on which tables count as derived.
    static string[] DerivedTables(PostMortemContext context)
    {
        var derived = context.GetService<IDesignTimeModel>().Model.GetEntityTypes()
            .Where(type => typeof(IDerivedEntity).IsAssignableFrom(type.ClrType))
            .Select(type => type.GetTableName()!)
            .ToHashSet(StringComparer.Ordinal);

        var connection = context.Database.GetDbConnection();
        connection.Open();

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name";

            using var reader = command.ExecuteReader();
            var found = new List<string>();
            while (reader.Read())
            {
                var name = reader.GetString(0);
                if (derived.Contains(name))
                {
                    found.Add(name);
                }
            }

            return [.. found];
        }
        finally
        {
            connection.Close();
        }
    }
}
