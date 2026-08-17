using AecoPostMortem.Data.Execution;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace AecoPostMortem.Data.Tests;

/// <summary>
/// The derived layer's shape, read from the model rather than from the database — these hold
/// whether or not a table has been created yet.
/// </summary>
public sealed class DerivedModelTests
{
    [Fact]
    public void Every_derived_entity_type_is_excluded_from_migrations()
    {
        using var context = new PostMortemContext();

        // context.Model returns the read-optimized runtime model, which strips migrations-only
        // annotations and makes IsTableExcludedFromMigrations() throw; the design-time model
        // still carries them.
        var designTimeModel = context.GetService<IDesignTimeModel>().Model;

        var included = designTimeModel.GetEntityTypes()
            .Where(type => typeof(IDerivedEntity).IsAssignableFrom(type.ClrType))
            .Where(type => !type.IsTableExcludedFromMigrations())
            .Select(type => type.ClrType.Name)
            .ToArray();

        Assert.True(
            included.Length == 0,
            "NORMALIZED and FINDINGS are re-derived from RAW, never migrated (Repo Rule 4, PRD "
            + "§3.8). These types would be picked up by the next `migrations add`: "
            + string.Join(", ", included));
    }

    [Fact]
    public void The_session_entity_is_derived()
    {
        using var context = new PostMortemContext();

        var session = context.Model.FindEntityType(typeof(Session));

        Assert.NotNull(session);
        Assert.Equal("session", session.GetTableName());
        Assert.True(typeof(IDerivedEntity).IsAssignableFrom(session.ClrType));
    }

    /// <summary>
    /// Named literally rather than read back from the mapping: a test that took its expectations
    /// from the code it checks would pass an index being renamed out of existence. Their absence
    /// was measured at 776.06 ms against 56.15 ms on Postgres for the per-tool aggregate — a
    /// measured 13.8× — falling to a measured 64.34 ms once present
    /// (docs/product-superpowers/research/2026-08-16-sqlite-vs-postgres-query-latency.md Part 3).
    /// </summary>
    [Theory]
    [InlineData("ix_tc_session", "session_id")]
    [InlineData("ix_tc_name", "tool_name")]
    [InlineData("ix_tc_session_path", "session_id,path")]
    [InlineData("ix_tc_name_success", "tool_name,success")]
    [InlineData("ix_tc_session_name", "session_id,tool_name")]
    public void The_measured_tool_call_index_exists(string name, string columns)
    {
        using var context = new PostMortemContext();

        var designTimeModel = context.GetService<IDesignTimeModel>().Model;

        var index = designTimeModel.FindEntityType(typeof(ToolCall))!
            .GetIndexes()
            .SingleOrDefault(candidate => candidate.GetDatabaseName() == name);

        Assert.True(index is not null, $"tool_call has no index '{name}'.");
        Assert.Equal(
            columns.Split(','),
            index!.Properties.Select(property => property.GetColumnName()).ToArray());
    }
}
