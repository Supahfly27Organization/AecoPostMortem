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
    /// The version is a hash of these statements (see
    /// <see cref="The_version_is_the_same_every_time_it_is_computed"/>), so a change here is what
    /// moves it; this test checks the statements themselves carry the pieces the contract depends
    /// on — a table, both check constraints, and a measured index.
    /// </summary>
    [Fact]
    public void The_generated_statements_carry_the_tables_constraints_and_indexes()
    {
        using var context = new PostMortemContext();

        var statements = DerivedSchema.CreateStatements(context);

        Assert.Contains(statements, sql => sql.Contains("CREATE TABLE IF NOT EXISTS turn", StringComparison.Ordinal));
        Assert.Contains(statements, sql => sql.Contains("ck_turn_owner", StringComparison.Ordinal));
        Assert.Contains(statements, sql => sql.Contains("ck_agent_cost", StringComparison.Ordinal));
        Assert.Contains(statements, sql => sql.Contains("ix_tc_name_success", StringComparison.Ordinal));
    }

    /// <summary>
    /// <see cref="DerivedSchema.CreateStatements"/> hand-emits exactly five relational features:
    /// columns (type and nullability), the primary key, check constraints, and indexes (including
    /// uniqueness). It is a partial reimplementation of EF's relational model, not a full one, and
    /// <c>ExcludeFromMigrations</c> means these entities never pass through EF's own script
    /// generator — the one place a genuinely unsupported mapping would otherwise be caught. So a
    /// later task that maps a default value, a computed column, a collation or a foreign key onto a
    /// derived entity would have it silently dropped from the generated DDL: the table would still
    /// get created, <see cref="DerivedSchema.EnsureCurrent"/> would still succeed, and nothing would
    /// fail to say the feature never reached SQLite. This test is the failure mode's guard: it walks
    /// every derived entity's design-time metadata and fails the moment one of them uses a feature
    /// the generator does not emit.
    /// </summary>
    [Fact]
    public void The_generator_covers_every_mapping_feature_the_derived_model_uses()
    {
        using var context = new PostMortemContext();

        var derivedTypes = context.GetService<IDesignTimeModel>().Model.GetEntityTypes()
            .Where(type => typeof(IDerivedEntity).IsAssignableFrom(type.ClrType));

        foreach (var entityType in derivedTypes)
        {
            foreach (var property in entityType.GetProperties())
            {
                var label = $"{entityType.ClrType.Name}.{property.Name}";

                // Not property.GetDefaultValue() is null: for a required, ValueGenerated.Never,
                // value-typed property (every enum column here, via HasConversion<string>()) that
                // extension falls back to the CLR default (e.g. OwnerKind.Main) even when nothing
                // was ever configured — confirmed empirically and by FindAnnotation returning null
                // for every one of them. Reading the annotation directly is the only way to ask "was
                // .HasDefaultValue(...) actually called" rather than "what would EF report as this
                // property's implied default."
                Assert.True(
                    property.FindAnnotation(RelationalAnnotationNames.DefaultValue) is null,
                    $"{label} declares a default value, which DerivedSchema.CreateStatements does "
                    + "not emit. Add it to the generator or drop the mapping.");

                Assert.True(
                    property.GetDefaultValueSql() is null,
                    $"{label} declares a default value SQL, which DerivedSchema.CreateStatements "
                    + "does not emit. Add it to the generator or drop the mapping.");

                Assert.True(
                    property.GetComputedColumnSql() is null,
                    $"{label} is a computed column, which DerivedSchema.CreateStatements does not "
                    + "emit. Add it to the generator or drop the mapping.");

                Assert.True(
                    property.GetCollation() is null,
                    $"{label} declares a collation, which DerivedSchema.CreateStatements does not "
                    + "emit. Add it to the generator or drop the mapping.");
            }

            Assert.True(
                !entityType.GetForeignKeys().Any(),
                $"{entityType.ClrType.Name} declares a foreign key, which DerivedSchema."
                + "CreateStatements does not emit. Add it to the generator or drop the mapping.");

            foreach (var index in entityType.GetIndexes())
            {
                // Unlike the other four checks, uniqueness IS already emitted (CreateStatements
                // prefixes the CREATE INDEX with UNIQUE when index.IsUnique is set) — this branch
                // exists so a new unique index is noticed and its generated statement verified by
                // eye, not because the feature is unsupported.
                Assert.False(
                    index.IsUnique,
                    $"{entityType.ClrType.Name}'s index '{index.GetDatabaseName()}' is unique. "
                    + "DerivedSchema.CreateStatements already emits UNIQUE for a unique index — "
                    + "confirm the generated CREATE INDEX statement carries it, then update this "
                    + "guard's expectation for the new index.");
            }
        }
    }

    /// <summary>S-46's rebuild mechanism: rows in the derived layer do not survive it, because they
    /// are re-derivable from RAW rather than something the rebuild is required to preserve.</summary>
    [Fact]
    public void Rebuild_drops_the_derived_rows_and_recreates_the_tables()
    {
        using var temporary = new TemporaryStore();

        using (var context = temporary.Store.Open())
        {
            context.Sessions.Add(new Session
            {
                SessionId = "session-1",
                StartedAt = "2026-08-09T20:14:36.758Z",
                CopilotVersion = "0.0.339",
                EventSchemaVersion = "1",
                SourceFile = @"~/.copilot/session-state/session-1/events.jsonl",
                Cwd = @"C:\repo",
            });
            context.SaveChanges();

            DerivedSchema.Rebuild(context);

            Assert.Empty(context.Sessions);
            Assert.Equal(8, DerivedTables(context).Length);
        }
    }

    /// <summary>Scenario "The operator can invoke the rebuild" (issue #24): RAW is what the rebuild
    /// re-derives from, so it is not itself a target of the drop.</summary>
    [Fact]
    public void Rebuild_leaves_RAW_unchanged()
    {
        using var temporary = new TemporaryStore();
        using var context = temporary.Store.Open();

        context.RawEvents.Add(new RawEvent(
            "session-1", 0, "session.start", "2026-08-09T20:14:36.758Z", "0.0.339",
            @"~/.copilot/session-state/session-1/events.jsonl", 0,
            RawPayload.ContentHashOfText("{}"), "{}"));
        context.SaveChanges();

        var before = context.RawEvents.AsNoTracking().ToArray();

        DerivedSchema.Rebuild(context);

        var after = context.RawEvents.AsNoTracking().ToArray();

        Assert.Equal(before.Select(row => row.Id), after.Select(row => row.Id));
        Assert.Equal(before.Select(row => row.ContentHash), after.Select(row => row.ContentHash));
        Assert.Equal(before.Select(row => row.Payload), after.Select(row => row.Payload));
    }

    /// <summary>The determinism contract's edge case (issue #24): order matters as much as content.
    /// Two rebuilds against the same model must produce the same tables, in the same order, with the
    /// same version — a tie broken arbitrarily between runs is exactly what §3.8 forbids.</summary>
    [Fact]
    public void Rebuilding_twice_produces_identical_schema_content_and_order()
    {
        using var temporary = new TemporaryStore();
        using var context = temporary.Store.Open();

        DerivedSchema.Rebuild(context);
        var firstStatements = DerivedSchema.CreateStatements(context);
        var firstTables = DerivedTables(context);
        var firstVersion = DerivedSchema.Version(context);

        DerivedSchema.Rebuild(context);
        var secondStatements = DerivedSchema.CreateStatements(context);
        var secondTables = DerivedTables(context);
        var secondVersion = DerivedSchema.Version(context);

        Assert.Equal(firstStatements, secondStatements);
        Assert.Equal(firstTables, secondTables);
        Assert.Equal(firstVersion, secondVersion);
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
                EventId = "e1",
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
