using Microsoft.EntityFrameworkCore;

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
    }
}
