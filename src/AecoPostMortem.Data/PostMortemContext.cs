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

    public DbSet<StoreMetadata> StoreMetadata => Set<StoreMetadata>();

    public DbSet<SystemPromptText> SystemPromptTexts => Set<SystemPromptText>();

    public DbSet<Session> Sessions => Set<Session>();

    public DbSet<Turn> Turns => Set<Turn>();

    public DbSet<ToolCall> ToolCalls => Set<ToolCall>();

    public DbSet<Agent> Agents => Set<Agent>();

    public DbSet<Skill> Skills => Set<Skill>();

    public DbSet<Hook> Hooks => Set<Hook>();

    public DbSet<Permission> Permissions => Set<Permission>();

    public DbSet<WriteUnit> WriteUnits => Set<WriteUnit>();

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

        MapStoreMetadata(modelBuilder);
        MapSystemPromptText(modelBuilder);
        MapSession(modelBuilder);
        MapTurn(modelBuilder);
        MapToolCall(modelBuilder);
        MapAgent(modelBuilder);
        MapEventScopedEntities(modelBuilder);
        ExcludeDerivedTypesFromMigrations(modelBuilder);
    }

    static void MapStoreMetadata(ModelBuilder modelBuilder)
    {
        var metadata = modelBuilder.Entity<StoreMetadata>();

        metadata.ToTable("store_metadata");
        metadata.HasKey(row => row.Key);

        metadata.Property(row => row.Key).HasColumnName("key");
        metadata.Property(row => row.Value).HasColumnName("value");
    }

    /// <summary>
    /// FR-12's dedup table. Migrated deliberately, like <c>store_metadata</c>: it is written at
    /// ingest time from source bytes, not re-derived from what the store already holds, so Repo
    /// Rule 4's "never migrated" applies to NORMALIZED and FINDINGS, not to this.
    /// </summary>
    static void MapSystemPromptText(ModelBuilder modelBuilder)
    {
        var text = modelBuilder.Entity<SystemPromptText>();

        text.ToTable(SystemPromptTextSchema.Table);
        text.HasKey(row => row.ContentHash);

        text.Property(row => row.ContentHash).HasColumnName(SystemPromptTextSchema.ContentHash).IsRequired();
        text.Property(row => row.Text).HasColumnName(SystemPromptTextSchema.Text).IsRequired();
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

    static void MapToolCall(ModelBuilder modelBuilder)
    {
        var toolCall = modelBuilder.Entity<ToolCall>();

        toolCall.ToTable("tool_call");
        toolCall.HasKey(row => new { row.SessionId, row.ToolCallId });

        toolCall.Property(row => row.SessionId).HasColumnName("session_id");
        toolCall.Property(row => row.ToolCallId).HasColumnName("tool_call_id");
        toolCall.Property(row => row.ToolName).HasColumnName("tool_name");
        toolCall.Property(row => row.StartedAt).HasColumnName("started_at");
        toolCall.Property(row => row.CompletedAt).HasColumnName("completed_at");
        toolCall.Property(row => row.Success).HasColumnName("success");
        toolCall.Property(row => row.Path).HasColumnName("path");
        toolCall.Property(row => row.ResultSizeBytes).HasColumnName("result_size_bytes");
        toolCall.Property(row => row.McpServerName).HasColumnName("mcp_server_name");
        toolCall.Property(row => row.McpToolName).HasColumnName("mcp_tool_name");
        toolCall.Property(row => row.TurnId).HasColumnName("turn_id");

        toolCall.HasIndex(row => row.SessionId).HasDatabaseName("ix_tc_session");
        toolCall.HasIndex(row => row.ToolName).HasDatabaseName("ix_tc_name");
        toolCall.HasIndex(row => new { row.SessionId, row.Path }).HasDatabaseName("ix_tc_session_path");
        toolCall.HasIndex(row => new { row.ToolName, row.Success }).HasDatabaseName("ix_tc_name_success");
        toolCall.HasIndex(row => new { row.SessionId, row.ToolName }).HasDatabaseName("ix_tc_session_name");

        MapOwnership(toolCall, "tool_call");
    }

    static void MapAgent(ModelBuilder modelBuilder)
    {
        var agent = modelBuilder.Entity<Agent>();

        agent.HasKey(row => new { row.SessionId, row.AgentId });

        agent.Property(row => row.SessionId).HasColumnName("session_id");
        agent.Property(row => row.AgentId).HasColumnName("agent_id");
        agent.Property(row => row.SpawningToolCallId).HasColumnName("spawning_tool_call_id");
        agent.Property(row => row.ParentAgentId).HasColumnName("parent_agent_id");
        agent.Property(row => row.Name).HasColumnName("name");
        agent.Property(row => row.DisplayName).HasColumnName("display_name");
        agent.Property(row => row.Description).HasColumnName("description");
        agent.Property(row => row.StartedAt).HasColumnName("started_at");
        agent.Property(row => row.TotalTokens).HasColumnName("total_tokens");
        agent.Property(row => row.TotalToolCalls).HasColumnName("total_tool_calls");
        agent.Property(row => row.DurationMs).HasColumnName("duration_ms");
        agent.Property(row => row.Model).HasColumnName("model");
        agent.Property(row => row.Error).HasColumnName("error");
        agent.Property(row => row.Outcome)
            .HasColumnName("outcome")
            .HasConversion<string>()
            .IsRequired();

        agent.HasIndex(row => row.SessionId).HasDatabaseName("ix_agent_session");
        agent.HasIndex(row => new { row.SessionId, row.ParentAgentId }).HasDatabaseName("ix_agent_parent");

        agent.ToTable("agent", table => table.HasCheckConstraint(
            "ck_agent_cost",
            "outcome = 'Completed' OR (total_tokens IS NULL AND total_tool_calls IS NULL "
            + "AND duration_ms IS NULL AND model IS NULL)"));
    }

    static void MapEventScopedEntities(ModelBuilder modelBuilder)
    {
        var skill = modelBuilder.Entity<Skill>();
        skill.HasKey(row => new { row.SessionId, row.EventId });
        skill.Property(row => row.SessionId).HasColumnName("session_id");
        skill.Property(row => row.EventId).HasColumnName("event_id");
        skill.Property(row => row.Name).HasColumnName("name");
        skill.Property(row => row.Path).HasColumnName("path");
        skill.Property(row => row.Description).HasColumnName("description");
        skill.Property(row => row.PluginName).HasColumnName("plugin_name");
        skill.Property(row => row.PluginVersion).HasColumnName("plugin_version");
        skill.Property(row => row.InvokedAt).HasColumnName("invoked_at");
        skill.HasIndex(row => row.SessionId).HasDatabaseName("ix_skill_session");
        MapOwnership(skill, "skill");

        var hook = modelBuilder.Entity<Hook>();
        hook.HasKey(row => new { row.SessionId, row.EventId });
        hook.Property(row => row.SessionId).HasColumnName("session_id");
        hook.Property(row => row.EventId).HasColumnName("event_id");
        hook.Property(row => row.Name).HasColumnName("name");
        hook.Property(row => row.StartedAt).HasColumnName("started_at");
        hook.Property(row => row.EndedAt).HasColumnName("ended_at");
        hook.Property(row => row.Success).HasColumnName("success");
        hook.HasIndex(row => row.SessionId).HasDatabaseName("ix_hook_session");
        MapOwnership(hook, "hook");

        var permission = modelBuilder.Entity<Permission>();
        permission.HasKey(row => new { row.SessionId, row.EventId });
        permission.Property(row => row.SessionId).HasColumnName("session_id");
        permission.Property(row => row.EventId).HasColumnName("event_id");
        permission.Property(row => row.RequestedAt).HasColumnName("requested_at");
        permission.Property(row => row.CompletedAt).HasColumnName("completed_at");
        permission.Property(row => row.ResultKind).HasColumnName("result_kind");
        permission.Property(row => row.ToolCallId).HasColumnName("tool_call_id");
        permission.HasIndex(row => row.SessionId).HasDatabaseName("ix_permission_session");
        MapOwnership(permission, "permission");

        var writeUnit = modelBuilder.Entity<WriteUnit>();
        writeUnit.HasKey(row => new { row.SessionId, row.EventId });
        writeUnit.Property(row => row.SessionId).HasColumnName("session_id");
        writeUnit.Property(row => row.EventId).HasColumnName("event_id");
        writeUnit.Property(row => row.ToolCallId).HasColumnName("tool_call_id");
        writeUnit.Property(row => row.Path).HasColumnName("path");
        writeUnit.Property(row => row.AddedContent).HasColumnName("added_content");
        writeUnit.HasIndex(row => row.SessionId).HasDatabaseName("ix_write_unit_session");
        MapOwnership(writeUnit, "write_unit");
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
