namespace AecoPostMortem.Data;

/// <summary>
/// The store's own state, as key and value. Migrated rather than derived: it records facts about
/// the store — including the derived schema's version — and a value dropped alongside the tables it
/// describes could not be compared against them.
/// </summary>
public sealed record StoreMetadata
{
    /// <summary>The derived schema's version. When the stored value differs from the computed one,
    /// the derived tables are dropped and recreated rather than migrated (PRD §3.8).</summary>
    public const string DerivedSchemaVersionKey = "derived_schema_version";

    public required string Key { get; init; }

    public required string Value { get; init; }
}
