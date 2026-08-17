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
}
