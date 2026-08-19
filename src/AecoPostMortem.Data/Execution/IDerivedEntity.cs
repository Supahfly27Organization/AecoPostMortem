namespace AecoPostMortem.Data.Execution;

/// <summary>
/// Marks an entity as belonging to a derived layer — re-derivable from RAW, and therefore never
/// migrated (Repo Rule 4, PRD §3.8). <see cref="PostMortemContext.OnModelCreating"/> enumerates
/// every type carrying this and excludes it from migrations, so the rule is a loop rather than a
/// call each new entity has to remember.
/// </summary>
public interface IDerivedEntity;
