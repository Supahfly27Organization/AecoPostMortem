using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace AecoPostMortem.Data.Execution;

/// <summary>
/// The derived layer's DDL, generated from the model rather than from a migration — because
/// NORMALIZED and FINDINGS are re-derived from RAW and never migrated (Repo Rule 4, PRD §3.8).
/// </summary>
/// <remarks>
/// The statements are built by hand from the model's metadata rather than by EF Core. The
/// <c>ExcludeFromMigrations</c> flag that keeps these tables out of migrations also makes
/// <c>IMigrationsModelDiffer</c> skip them, so every EF-generated script — including
/// <c>GenerateCreateScript</c> — omits them. Generating here is also what lets
/// <see cref="Version"/> hash exactly the DDL that runs.
///
/// Every read of the model in this type goes through <c>context.GetService&lt;IDesignTimeModel&gt;().Model</c>
/// rather than <c>context.Model</c>. <c>context.Model</c> is EF Core 10's read-optimized runtime
/// model, which strips migrations-era annotations — check constraints among them — so building the
/// DDL from it would silently emit tables with no <c>CHECK</c> clause at all, defeating the
/// ownership-pairing and agent-cost invariants this contract exists to enforce (see
/// <c>DerivedModelTests</c> and <c>src/AecoPostMortem.Data/CLAUDE.md</c> for the same rule
/// elsewhere).
/// </remarks>
public static class DerivedSchema
{
    /// <summary>Every derived table's <c>CREATE</c>, in a fixed order so the version is stable.</summary>
    public static IReadOnlyList<string> CreateStatements(PostMortemContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var statements = new List<string>();

        foreach (var entityType in DerivedEntityTypes(context))
        {
            var table = entityType.GetTableName()!;

            var lines = entityType.GetProperties()
                .OrderBy(property => property.GetColumnName(), StringComparer.Ordinal)
                .Select(property =>
                    $"  {property.GetColumnName()} {property.GetColumnType()}"
                    + (property.IsNullable ? string.Empty : " NOT NULL"))
                .ToList();

            var key = entityType.FindPrimaryKey()!;
            lines.Add($"  PRIMARY KEY ({string.Join(", ", key.Properties.Select(p => p.GetColumnName()))})");

            lines.AddRange(entityType.GetCheckConstraints()
                .OrderBy(constraint => constraint.Name, StringComparer.Ordinal)
                .Select(constraint => $"  CONSTRAINT {constraint.Name} CHECK ({constraint.Sql})"));

            statements.Add($"CREATE TABLE IF NOT EXISTS {table} (\n{string.Join(",\n", lines)}\n)");

            statements.AddRange(entityType.GetIndexes()
                .OrderBy(index => index.GetDatabaseName(), StringComparer.Ordinal)
                .Select(index =>
                    $"CREATE {(index.IsUnique ? "UNIQUE " : string.Empty)}INDEX IF NOT EXISTS "
                    + $"{index.GetDatabaseName()} ON {table} "
                    + $"({string.Join(", ", index.Properties.Select(p => p.GetColumnName()))})"));
        }

        return statements;
    }

    public static IReadOnlyList<string> DropStatements(PostMortemContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return DerivedEntityTypes(context)
            .Select(entityType => $"DROP TABLE IF EXISTS {entityType.GetTableName()}")
            .ToArray();
    }

    /// <summary>
    /// The derived schema's version: SHA-256 over the statements, lower-case hex. Computed rather
    /// than maintained, so it cannot be forgotten when a column changes.
    /// </summary>
    public static string Version(PostMortemContext context) =>
        Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(";\n", CreateStatements(context)))));

    public static void Create(PostMortemContext context) =>
        Execute(context, CreateStatements(context));

    public static void Drop(PostMortemContext context) =>
        Execute(context, DropStatements(context));

    /// <summary>
    /// Bring the derived tables in line with the model. A version that differs from the stored one
    /// means the tables predate the model, so they are dropped and recreated — the rows go with
    /// them, which is exactly what §3.8 intends: they are re-derivable from RAW, and `rebuild`
    /// re-derives them.
    /// </summary>
    public static void EnsureCurrent(PostMortemContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var expected = Version(context);
        var recorded = context.StoreMetadata
            .AsNoTracking()
            .FirstOrDefault(row => row.Key == StoreMetadata.DerivedSchemaVersionKey)?.Value;

        if (recorded != expected)
        {
            Drop(context);
        }

        Create(context);

        if (recorded == expected)
        {
            return;
        }

        context.Database.ExecuteSqlRaw(
            "INSERT INTO store_metadata (key, value) VALUES ({0}, {1}) "
            + "ON CONFLICT (key) DO UPDATE SET value = excluded.value",
            StoreMetadata.DerivedSchemaVersionKey,
            expected);
    }

    static IEnumerable<IEntityType> DerivedEntityTypes(PostMortemContext context) =>
        context.GetService<IDesignTimeModel>().Model.GetEntityTypes()
            .Where(type => typeof(IDerivedEntity).IsAssignableFrom(type.ClrType))
            .OrderBy(type => type.GetTableName(), StringComparer.Ordinal);

    static void Execute(PostMortemContext context, IReadOnlyList<string> statements)
    {
        foreach (var sql in statements)
        {
            context.Database.ExecuteSqlRaw(sql);
        }
    }
}
