using AecoPostMortem.Data.Execution;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AecoPostMortem.Data;

/// <summary>
/// The store's only <see cref="DbContext"/>. It holds RAW today; NORMALIZED and FINDINGS join it as
/// their stories land, and they will do so without migrations — RAW is the only layer that gets one
/// (PRD §3.8), because it holds provider events that no longer exist at the source once Copilot's
/// window rotates, while the derived layers are re-derived from it instead.
/// </summary>
public sealed class PostMortemContext : DbContext
{
    /// <summary>
    /// The constructor the product uses. <see cref="LocalStore.Open"/> is what supplies the options,
    /// so the connection string is decided in one place.
    /// </summary>
    public PostMortemContext(DbContextOptions<PostMortemContext> options)
        : base(options)
    {
    }

    /// <summary>
    /// For <c>dotnet ef</c> only. The tooling instantiates the context to read the model, never to
    /// reach a database, so this points at a private in-memory one: a caller who reaches this
    /// constructor by accident gets an empty database that vanishes, not the operator's store.
    /// </summary>
    public PostMortemContext()
        : base(new DbContextOptionsBuilder<PostMortemContext>().UseSqlite("Data Source=:memory:").Options)
    {
    }

    public DbSet<RawEvent> RawEvents => Set<RawEvent>();

    public DbSet<Session> Sessions => Set<Session>();

    public DbSet<Turn> Turns => Set<Turn>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        var rawEvent = modelBuilder.Entity<RawEvent>();

        rawEvent.ToTable(RawEventSchema.Table);
        rawEvent.HasKey(row => row.Id);

        rawEvent.Property(row => row.Id).HasColumnName(RawEventSchema.Id).ValueGeneratedOnAdd();
        rawEvent.Property(row => row.SessionId).HasColumnName(RawEventSchema.SessionId).IsRequired();
        rawEvent.Property(row => row.Sequence).HasColumnName(RawEventSchema.Sequence);
        rawEvent.Property(row => row.EventType).HasColumnName(RawEventSchema.EventType).IsRequired();
        rawEvent.Property(row => row.Timestamp).HasColumnName(RawEventSchema.Timestamp).IsRequired();
        rawEvent.Property(row => row.ProviderVersion).HasColumnName(RawEventSchema.ProviderVersion).IsRequired();
        rawEvent.Property(row => row.SourceFile).HasColumnName(RawEventSchema.SourceFile).IsRequired();
        rawEvent.Property(row => row.ByteOffset).HasColumnName(RawEventSchema.ByteOffset);
        rawEvent.Property(row => row.ContentHash).HasColumnName(RawEventSchema.ContentHash).IsRequired();
        rawEvent.Property(row => row.Payload).HasColumnName(RawEventSchema.Payload).IsRequired();

        rawEvent
            .HasIndex(row => new { row.SourceFile, row.ByteOffset, row.ContentHash })
            .HasDatabaseName(RawEventSchema.IdentityIndex)
            .IsUnique();

        rawEvent
            .HasIndex(row => new { row.SessionId, row.Sequence })
            .HasDatabaseName(RawEventSchema.SessionSequenceIndex);

        rawEvent
            .HasIndex(row => row.EventType)
            .HasDatabaseName(RawEventSchema.EventTypeIndex);

        MapSession(modelBuilder);
        MapTurn(modelBuilder);
        ExcludeDerivedTypesFromMigrations(modelBuilder);
    }

    static void MapSession(ModelBuilder modelBuilder)
    {
        var session = modelBuilder.Entity<Session>();

        session.ToTable("session");
        session.HasKey(row => row.SessionId);

        session.Property(row => row.SessionId).HasColumnName("session_id");
        session.Property(row => row.StartedAt).HasColumnName("started_at");
        session.Property(row => row.EndedAt).HasColumnName("ended_at");
        session.Property(row => row.CopilotVersion).HasColumnName("copilot_version");
        session.Property(row => row.EventSchemaVersion).HasColumnName("event_schema_version");
        session.Property(row => row.SourceFile).HasColumnName("source_file");
        session.Property(row => row.Cwd).HasColumnName("cwd");
        session.Property(row => row.GitRoot).HasColumnName("git_root");
        session.Property(row => row.Branch).HasColumnName("branch");
        session.Property(row => row.HeadCommit).HasColumnName("head_commit");
        session.Property(row => row.Repository).HasColumnName("repository");
        session.Property(row => row.HostType).HasColumnName("host_type");
        session.Property(row => row.BaseCommit).HasColumnName("base_commit");
        session.Property(row => row.InputTokens).HasColumnName("input_tokens");
        session.Property(row => row.OutputTokens).HasColumnName("output_tokens");
        session.Property(row => row.CacheReadTokens).HasColumnName("cache_read_tokens");
        session.Property(row => row.CacheWriteTokens).HasColumnName("cache_write_tokens");
        session.Property(row => row.ReasoningTokens).HasColumnName("reasoning_tokens");
        session.Property(row => row.ModelCount).HasColumnName("model_count");
    }

    /// <summary>
    /// The ownership columns and the check that binds them. A row claiming the main thread while
    /// carrying an agent id — or claiming an agent without one — is refused by the database rather
    /// than by whoever wrote it.
    /// </summary>
    static void MapOwnership<TEntity>(EntityTypeBuilder<TEntity> entity, string table)
        where TEntity : class, IOwned
    {
        entity.Property(row => row.OwnerKind)
            .HasColumnName("owner_kind")
            .HasConversion(
                kind => kind == OwnerKind.Main ? "main" : "agent",
                text => text == "main" ? OwnerKind.Main : OwnerKind.Agent)
            .IsRequired();

        entity.Property(row => row.AgentId).HasColumnName("agent_id");

        entity.ToTable(table, builder => builder.HasCheckConstraint(
            $"ck_{table}_owner",
            "(owner_kind = 'main') = (agent_id IS NULL)"));
    }

    static void MapTurn(ModelBuilder modelBuilder)
    {
        var turn = modelBuilder.Entity<Turn>();

        turn.ToTable("turn");
        turn.HasKey(row => new { row.SessionId, row.TurnId });

        turn.Property(row => row.SessionId).HasColumnName("session_id");
        turn.Property(row => row.TurnId).HasColumnName("turn_id");
        turn.Property(row => row.StartedAt).HasColumnName("started_at");
        turn.Property(row => row.EndedAt).HasColumnName("ended_at");
        turn.Property(row => row.AbortReason).HasColumnName("abort_reason");
        turn.Property(row => row.OutputTokens).HasColumnName("output_tokens");
        turn.Property(row => row.Outcome)
            .HasColumnName("outcome")
            .HasConversion<string>()
            .IsRequired();

        turn.HasIndex(row => row.SessionId).HasDatabaseName("ix_turn_session");

        MapOwnership(turn, "turn");
    }

    /// <summary>
    /// Repo Rule 4 as a loop rather than as a call each new entity must remember. A derived type
    /// added in a year is caught by this; one that omits the marker fails
    /// <c>DerivedModelTests</c> instead of silently entering the migration set.
    /// </summary>
    static void ExcludeDerivedTypesFromMigrations(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes().ToList())
        {
            if (typeof(IDerivedEntity).IsAssignableFrom(entityType.ClrType))
            {
                modelBuilder.Entity(entityType.ClrType).ToTable(table => table.ExcludeFromMigrations());
            }
        }
    }
}
