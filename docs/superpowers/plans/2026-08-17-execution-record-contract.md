# Execution-record entity contract Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Publish the eight NORMALIZED entity shapes, their tables and their guarantees, so that fifteen downstream stories can build against a shape instead of queueing behind S-06.

**Architecture:** One `PostMortemContext` holds every entity. The eight derived types carry a marker interface; `OnModelCreating` loops over every type carrying it and excludes it from migrations, so RAW stays the only migrated model. The derived tables are created from DDL generated out of the model at open time, and a SHA-256 over that DDL is the schema version — when it moves, the derived tables are dropped and recreated rather than migrated.

**Tech Stack:** .NET 10, C#, EF Core 10 over SQLite (`Microsoft.EntityFrameworkCore.Sqlite`), xUnit v3.

**Spec:** `docs/superpowers/specs/2026-08-17-execution-record-contract-design.md`

## Global Constraints

- Target framework `net10.0`; `Nullable` enabled; `TreatWarningsAsErrors` true; `EnforceCodeStyleInBuild` true. A warning fails the build.
- Tables and columns are `snake_case`. Types and properties are PascalCase.
- Timestamps are stored as `TEXT`, verbatim from the event, exactly as `RawEvent.Timestamp` already is. PRD §3.8 forbids a wall-clock dependency; ISO-8601 sorts lexically, so ordering needs no conversion.
- A field measured below full coverage is nullable and never zero-filled. A zero is a number a surface would print.
- Repo Rule 1: do not read `src/AecoPostMortem.Data/Migrations/` except in Task 6, which is explicitly about a migration.
- Repo Rule 4: only RAW carries a migration. Task 6 is the single exception and it adds `store_metadata`, which is not derived.
- Run `python scripts/check-claude-md.py <changed router>` after any `CLAUDE.md` edit; it must report no findings for the files you touched.
- Every commit message ends with:
  ```
  Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_0124SRYTKGtQkfg4gfUeeQZq
  ```
- Every commit summary line ends with ` (#12)`.
- Work happens on branch `s-49-execution-record-contract`, which already exists and holds the spec commit.
- Full test command: `dotnet test AecoPostMortem.sln --nologo -v q`. Single project: `dotnet test test/AecoPostMortem.Data.Tests/AecoPostMortem.Data.Tests.csproj --nologo -v q`.

### One deliberate refinement of the spec

The spec's §3.2 describes ownership as "a small value with `Ownership.MainThread` and `Ownership.By(agentId)`". This plan implements it as a shared `IOwned` interface carrying two mapped properties — `OwnerKind` (a non-nullable enum) and `AgentId` (nullable) — bound by a check constraint, with `IsMainThread()` as an extension.

The invariant is unchanged and still structural: `owner_kind` is NOT NULL, so there is no null a caller can misread as "attribution unknown". What changes is the C# surface. EF Core's complex-type mapping does not reliably bind constructors, and a contract fifteen stories depend on is the wrong place to bet on it.

## File Structure

**Created:**
- `src/AecoPostMortem.Data/Execution/IDerivedEntity.cs` — the marker the migration-exclusion loop reads
- `src/AecoPostMortem.Data/Execution/IOwned.cs` — `OwnerKind`, `IOwned`, `IsMainThread()`
- `src/AecoPostMortem.Data/Execution/Session.cs`
- `src/AecoPostMortem.Data/Execution/Turn.cs`
- `src/AecoPostMortem.Data/Execution/ToolCall.cs`
- `src/AecoPostMortem.Data/Execution/Agent.cs`
- `src/AecoPostMortem.Data/Execution/EventScopedEntities.cs` — `Skill`, `Hook`, `Permission`, `WriteUnit`; four small shapes that share a key form and change together
- `src/AecoPostMortem.Data/Execution/DerivedSchema.cs` — create, drop, version, `EnsureCurrent`
- `src/AecoPostMortem.Data/StoreMetadata.cs` — the one migrated non-RAW table
- `test/AecoPostMortem.Data.Tests/DerivedModelTests.cs` — the convention and the acceptance criteria
- `test/AecoPostMortem.Data.Tests/OwnershipTests.cs`
- `test/AecoPostMortem.Data.Tests/AgentOutcomeTests.cs`
- `test/AecoPostMortem.Data.Tests/DerivedSchemaTests.cs`

**Modified:**
- `src/AecoPostMortem.Data/PostMortemContext.cs` — register the eight, run the exclusion loop
- `src/AecoPostMortem.Data/LocalStore.cs` — `Open()` calls `DerivedSchema.EnsureCurrent`
- `test/AecoPostMortem.Data.Tests/SchemaTests.cs` — widen the migrated-table assertion to two tables
- `src/AecoPostMortem.Data/CLAUDE.md` — the derived-layer decisions and a playbook entry
- `docs/claude/DOMAIN_MODEL.md` — the eight entities

---

### Task 1: The derived marker, the exclusion convention, and `Session`

The convention has to exist before anything relies on it, and it needs one real entity to be testable rather than vacuous. `Session` is that entity: it is the only one of the eight that carries no ownership, so it exercises the convention without depending on Task 2.

**Files:**
- Create: `src/AecoPostMortem.Data/Execution/IDerivedEntity.cs`
- Create: `src/AecoPostMortem.Data/Execution/Session.cs`
- Modify: `src/AecoPostMortem.Data/PostMortemContext.cs`
- Test: `test/AecoPostMortem.Data.Tests/DerivedModelTests.cs`

**Interfaces:**
- Consumes: `PostMortemContext`, `LocalStore`, `TemporaryStore` (test helper) — all exist.
- Produces: `AecoPostMortem.Data.Execution.IDerivedEntity` (empty marker interface); `AecoPostMortem.Data.Execution.Session` with `required string SessionId`; `PostMortemContext.Sessions` as `DbSet<Session>`.

- [ ] **Step 1: Write the failing test**

Create `test/AecoPostMortem.Data.Tests/DerivedModelTests.cs`:

```csharp
using AecoPostMortem.Data.Execution;
using Microsoft.EntityFrameworkCore;

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

        var included = context.Model.GetEntityTypes()
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
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/AecoPostMortem.Data.Tests/AecoPostMortem.Data.Tests.csproj --nologo -v q`

Expected: FAIL to compile — `IDerivedEntity` and `Session` do not exist.

- [ ] **Step 3: Write minimal implementation**

Create `src/AecoPostMortem.Data/Execution/IDerivedEntity.cs`:

```csharp
namespace AecoPostMortem.Data.Execution;

/// <summary>
/// Marks an entity as belonging to a derived layer — re-derivable from RAW, and therefore never
/// migrated (Repo Rule 4, PRD §3.8). <see cref="PostMortemContext.OnModelCreating"/> enumerates
/// every type carrying this and excludes it from migrations, so the rule is a loop rather than a
/// call each new entity has to remember.
/// </summary>
public interface IDerivedEntity;
```

Create `src/AecoPostMortem.Data/Execution/Session.cs`:

```csharp
namespace AecoPostMortem.Data.Execution;

/// <summary>
/// One Copilot session. The scope every other derived entity is keyed within, which is why it is
/// the only one of the eight that carries no ownership: it is the scope rather than a thing owned.
/// </summary>
/// <remarks>
/// The token figures come from <c>session.shutdown.data.modelMetrics</c>, measured present on 31 of
/// 35 sessions — so they are nullable and never zero-filled, because a zero is a number a surface
/// would print. They are summed across models; <see cref="ModelCount"/> says how many were summed.
/// The per-model breakdown is a known gap, recorded in the design rather than hidden.
/// </remarks>
public sealed record Session : IDerivedEntity
{
    public required string SessionId { get; init; }

    public required string StartedAt { get; init; }

    /// <summary>Null when the session never wrote <c>session.shutdown</c> — measured 31 of 35 did.</summary>
    public string? EndedAt { get; init; }

    public required string CopilotVersion { get; init; }

    public required string EventSchemaVersion { get; init; }

    public required string SourceFile { get; init; }

    // session.start.data.context, measured present on 35 of 35 sessions.
    public required string Cwd { get; init; }

    public string? GitRoot { get; init; }

    public string? Branch { get; init; }

    public string? HeadCommit { get; init; }

    public string? Repository { get; init; }

    public string? HostType { get; init; }

    public string? BaseCommit { get; init; }

    public long? InputTokens { get; init; }

    public long? OutputTokens { get; init; }

    public long? CacheReadTokens { get; init; }

    public long? CacheWriteTokens { get; init; }

    public long? ReasoningTokens { get; init; }

    public int? ModelCount { get; init; }
}
```

Modify `src/AecoPostMortem.Data/PostMortemContext.cs` — add `using AecoPostMortem.Data.Execution;` at the top, add the `DbSet` beside `RawEvents`:

```csharp
    public DbSet<Session> Sessions => Set<Session>();
```

and append to the end of `OnModelCreating`, after the `rawEvent` mapping:

```csharp
        MapSession(modelBuilder);
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
```

Delete the now-duplicated closing brace of the original `OnModelCreating` if you introduced one — `OnModelCreating` must end with the two calls above and nothing else.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test test/AecoPostMortem.Data.Tests/AecoPostMortem.Data.Tests.csproj --nologo -v q`

Expected: PASS, including the pre-existing `SchemaTests.RAW_is_the_only_table_the_migrations_create` — `session` is excluded from migrations, so the migration set is still exactly `raw_event`. If that test fails, the exclusion loop is not running.

- [ ] **Step 5: Commit**

```bash
git add src/AecoPostMortem.Data/Execution src/AecoPostMortem.Data/PostMortemContext.cs test/AecoPostMortem.Data.Tests/DerivedModelTests.cs
git commit -m "Mark the derived layer and keep it out of migrations by convention (#12)" -m "Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_0124SRYTKGtQkfg4gfUeeQZq"
```

---

### Task 2: Ownership, and `Turn`

**Files:**
- Create: `src/AecoPostMortem.Data/Execution/IOwned.cs`
- Create: `src/AecoPostMortem.Data/Execution/Turn.cs`
- Modify: `src/AecoPostMortem.Data/PostMortemContext.cs`
- Test: `test/AecoPostMortem.Data.Tests/OwnershipTests.cs`

**Interfaces:**
- Consumes: `IDerivedEntity` from Task 1.
- Produces: `OwnerKind` enum (`Main`, `Agent`); `IOwned` with `OwnerKind OwnerKind { get; init; }` and `string? AgentId { get; init; }`; `Owned.IsMainThread(this IOwned)`; `TurnOutcome` enum (`Unfinished`, `Completed`, `Aborted`); `Turn` keyed `(SessionId, TurnId)`; `PostMortemContext.Turns`.

The check constraint `ck_<table>_owner` is added by a shared helper later tasks reuse — its exact name and SQL are below and must not drift.

- [ ] **Step 1: Write the failing test**

Create `test/AecoPostMortem.Data.Tests/OwnershipTests.cs`:

```csharp
using AecoPostMortem.Data.Execution;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AecoPostMortem.Data.Tests;

/// <summary>
/// Acceptance criterion 3: an event carrying no agent id means main thread, exactly — the data map
/// measured 115 of 115 agent ids resolving to a known subagent handle. So the shape says "main
/// thread" rather than leaving a null for a reader to guess at.
/// </summary>
public sealed class OwnershipTests
{
    [Fact]
    public void The_owner_kind_column_is_not_nullable()
    {
        using var context = new PostMortemContext();

        var ownerKind = context.Model.FindEntityType(typeof(Turn))!
            .GetProperties()
            .Single(property => property.GetColumnName() == "owner_kind");

        Assert.False(
            ownerKind.IsNullable,
            "owner_kind must be NOT NULL: a nullable one is exactly the null the criterion forbids.");
    }

    [Fact]
    public void Main_thread_ownership_carries_no_agent_id()
    {
        Assert.Equal(OwnerKind.Main, MainThreadTurn().OwnerKind);
        Assert.Null(MainThreadTurn().AgentId);
        Assert.True(MainThreadTurn().IsMainThread());
    }

    [Fact]
    public void Agent_ownership_carries_one()
    {
        var owned = MainThreadTurn() with { OwnerKind = OwnerKind.Agent, AgentId = "call_42" };

        Assert.False(owned.IsMainThread());
        Assert.Equal("call_42", owned.AgentId);
    }

    /// <summary>The pairing is enforced by the database, not by whoever writes the row.</summary>
    [Theory]
    [InlineData(OwnerKind.Main, "call_42")]
    [InlineData(OwnerKind.Agent, null)]
    public void A_mismatched_pair_is_refused_by_the_store(OwnerKind kind, string? agentId)
    {
        using var temporary = new TemporaryStore();
        using var context = temporary.Store.Open();

        context.Turns.Add(MainThreadTurn() with { OwnerKind = kind, AgentId = agentId });

        Assert.Throws<DbUpdateException>(() => context.SaveChanges());
    }

    [Fact]
    public void A_matched_pair_is_accepted()
    {
        using var temporary = new TemporaryStore();
        using var context = temporary.Store.Open();

        context.Turns.Add(MainThreadTurn());
        context.SaveChanges();

        Assert.Equal(1, context.Turns.Count());
    }

    static Turn MainThreadTurn() => new()
    {
        SessionId = "session-1",
        TurnId = "turn-1",
        StartedAt = "2026-08-09T20:14:36.758Z",
        Outcome = TurnOutcome.Completed,
        OwnerKind = OwnerKind.Main,
    };
}
```

Note: `A_mismatched_pair_is_refused_by_the_store` and `A_matched_pair_is_accepted` need the `turn` table to exist, which arrives in Task 7. They will fail until then — that is expected and is recorded in Task 7's Step 4. The other three tests must pass at the end of this task.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/AecoPostMortem.Data.Tests/AecoPostMortem.Data.Tests.csproj --nologo -v q`

Expected: FAIL to compile — `OwnerKind`, `IOwned`, `Turn` and `TurnOutcome` do not exist.

- [ ] **Step 3: Write minimal implementation**

Create `src/AecoPostMortem.Data/Execution/IOwned.cs`:

```csharp
namespace AecoPostMortem.Data.Execution;

/// <summary>Who ran the thing: the main thread, or a named subagent.</summary>
public enum OwnerKind
{
    Main,
    Agent,
}

/// <summary>
/// Carried by every derived entity that a subagent can own. <see cref="OwnerKind"/> is not nullable,
/// so there is no null a caller could read as "attribution unknown" — absence of an agent id means
/// main thread, exactly, and the data map measured that rather than assuming it.
/// </summary>
public interface IOwned
{
    OwnerKind OwnerKind { get; init; }

    string? AgentId { get; init; }
}

public static class Owned
{
    public static bool IsMainThread(this IOwned owned)
    {
        ArgumentNullException.ThrowIfNull(owned);
        return owned.OwnerKind == OwnerKind.Main;
    }
}
```

Create `src/AecoPostMortem.Data/Execution/Turn.cs`:

```csharp
namespace AecoPostMortem.Data.Execution;

/// <summary>How a turn ended. A measured 2,384 turn starts against 2,375 ends means unfinished is a
/// real state rather than a defensive one.</summary>
public enum TurnOutcome
{
    Unfinished,
    Completed,
    Aborted,
}

/// <summary>
/// One assistant turn, bounded by <c>assistant.turn_start</c> and <c>assistant.turn_end</c>.
/// </summary>
/// <remarks>
/// Message text is not here. The latency research measured the Flight Recorder's tape against
/// <c>raw_event</c> directly, so NORMALIZED holds the execution skeleton and messages are read from
/// RAW.
/// </remarks>
public sealed record Turn : IDerivedEntity, IOwned
{
    public required string SessionId { get; init; }

    public required string TurnId { get; init; }

    public required string StartedAt { get; init; }

    public string? EndedAt { get; init; }

    public required TurnOutcome Outcome { get; init; }

    /// <summary>Set only when <see cref="Outcome"/> is <see cref="TurnOutcome.Aborted"/>.</summary>
    public string? AbortReason { get; init; }

    public long? OutputTokens { get; init; }

    public required OwnerKind OwnerKind { get; init; }

    public string? AgentId { get; init; }
}
```

Modify `src/AecoPostMortem.Data/PostMortemContext.cs`. Add the `DbSet`:

```csharp
    public DbSet<Turn> Turns => Set<Turn>();
```

Add these two shared helpers (every later task reuses them, so they are written once here):

```csharp
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
```

Add `using Microsoft.EntityFrameworkCore.Metadata.Builders;` to the file's usings, and call `MapTurn(modelBuilder);` in `OnModelCreating` immediately after `MapSession(modelBuilder);` and before `ExcludeDerivedTypesFromMigrations(modelBuilder);`.

- [ ] **Step 4: Run tests to verify the model-level ones pass**

Run: `dotnet test test/AecoPostMortem.Data.Tests/AecoPostMortem.Data.Tests.csproj --nologo -v q --filter "FullyQualifiedName~OwnershipTests.The_owner_kind|FullyQualifiedName~OwnershipTests.Main_thread|FullyQualifiedName~OwnershipTests.Agent_ownership"`

Expected: PASS, 3 tests. The two store-level tests still fail — the `turn` table does not exist until Task 7.

- [ ] **Step 5: Commit**

```bash
git add src/AecoPostMortem.Data/Execution src/AecoPostMortem.Data/PostMortemContext.cs test/AecoPostMortem.Data.Tests/OwnershipTests.cs
git commit -m "Make main-thread ownership a value the database enforces (#12)" -m "Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_0124SRYTKGtQkfg4gfUeeQZq"
```

---

### Task 3: `ToolCall` and its measured indexes

**Files:**
- Create: `src/AecoPostMortem.Data/Execution/ToolCall.cs`
- Modify: `src/AecoPostMortem.Data/PostMortemContext.cs`
- Test: `test/AecoPostMortem.Data.Tests/DerivedModelTests.cs`

**Interfaces:**
- Consumes: `IDerivedEntity`, `IOwned`, `MapOwnership` from Task 2.
- Produces: `ToolCall` keyed `(SessionId, ToolCallId)`; `PostMortemContext.ToolCalls`.

- [ ] **Step 1: Write the failing test**

Append to `test/AecoPostMortem.Data.Tests/DerivedModelTests.cs`:

```csharp
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

        var index = context.Model.FindEntityType(typeof(ToolCall))!
            .GetIndexes()
            .SingleOrDefault(candidate => candidate.GetDatabaseName() == name);

        Assert.True(index is not null, $"tool_call has no index '{name}'.");
        Assert.Equal(
            columns.Split(','),
            index!.Properties.Select(property => property.GetColumnName()).ToArray());
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/AecoPostMortem.Data.Tests/AecoPostMortem.Data.Tests.csproj --nologo -v q`

Expected: FAIL to compile — `ToolCall` does not exist.

- [ ] **Step 3: Write minimal implementation**

Create `src/AecoPostMortem.Data/Execution/ToolCall.cs`:

```csharp
namespace AecoPostMortem.Data.Execution;

/// <summary>
/// One tool invocation, from its <c>tool.execution_start</c> to the matching
/// <c>tool.execution_complete</c>. A measured 16,085 starts against 16,076 completions means an
/// unfinished call is a real state, so the completion fields are nullable.
/// </summary>
public sealed record ToolCall : IDerivedEntity, IOwned
{
    public required string SessionId { get; init; }

    public required string ToolCallId { get; init; }

    public required string ToolName { get; init; }

    public required string StartedAt { get; init; }

    public string? CompletedAt { get; init; }

    /// <summary>From <c>tool.execution_complete.data.success</c>, measured present on 16,076 of
    /// 16,076 completions — so null means "not completed", never "completed, outcome unknown".</summary>
    public bool? Success { get; init; }

    /// <summary>The path a read or write touched, measured present on 5,201 of 5,201 <c>view</c>
    /// calls. Null for tools that name no path.</summary>
    public string? Path { get; init; }

    public long? ResultSizeBytes { get; init; }

    public string? McpServerName { get; init; }

    public string? McpToolName { get; init; }

    public string? TurnId { get; init; }

    public required OwnerKind OwnerKind { get; init; }

    public string? AgentId { get; init; }
}
```

Modify `src/AecoPostMortem.Data/PostMortemContext.cs`. Add the `DbSet`:

```csharp
    public DbSet<ToolCall> ToolCalls => Set<ToolCall>();
```

Add the mapping and call `MapToolCall(modelBuilder);` after `MapTurn(modelBuilder);`:

```csharp
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
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test test/AecoPostMortem.Data.Tests/AecoPostMortem.Data.Tests.csproj --nologo -v q --filter "FullyQualifiedName~DerivedModelTests"`

Expected: PASS, 7 tests.

- [ ] **Step 5: Commit**

```bash
git add src/AecoPostMortem.Data/Execution/ToolCall.cs src/AecoPostMortem.Data/PostMortemContext.cs test/AecoPostMortem.Data.Tests/DerivedModelTests.cs
git commit -m "Publish the tool-call shape with the indexes the measurement demanded (#12)" -m "Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_0124SRYTKGtQkfg4gfUeeQZq"
```

---

### Task 4: `Agent` and its completion tri-state

**Files:**
- Create: `src/AecoPostMortem.Data/Execution/Agent.cs`
- Modify: `src/AecoPostMortem.Data/PostMortemContext.cs`
- Test: `test/AecoPostMortem.Data.Tests/AgentOutcomeTests.cs`

**Interfaces:**
- Consumes: `IDerivedEntity` from Task 1.
- Produces: `AgentOutcome` enum (`Running`, `Completed`, `CompletedCostUnknown`, `Failed`); `Agent` keyed `(SessionId, AgentId)`; `PostMortemContext.Agents`.

`Agent` implements `IDerivedEntity` only, not `IOwned` — it *is* the owner, and its own key column is already `agent_id`.

- [ ] **Step 1: Write the failing test**

Create `test/AecoPostMortem.Data.Tests/AgentOutcomeTests.cs`:

```csharp
using AecoPostMortem.Data.Execution;
using Microsoft.EntityFrameworkCore;

namespace AecoPostMortem.Data.Tests;

/// <summary>
/// The story's own edge case: <c>subagent.completed</c> carries tokens and duration on a measured
/// 215 of 462 completions, so "completed, cost unknown" has to be distinguishable from "did not
/// complete" — and neither may be readable as zero tokens.
/// </summary>
public sealed class AgentOutcomeTests
{
    [Fact]
    public void The_four_outcomes_are_distinct_states()
    {
        Assert.Equal(4, Enum.GetValues<AgentOutcome>().Length);
        Assert.Contains(AgentOutcome.Running, Enum.GetValues<AgentOutcome>());
        Assert.Contains(AgentOutcome.Completed, Enum.GetValues<AgentOutcome>());
        Assert.Contains(AgentOutcome.CompletedCostUnknown, Enum.GetValues<AgentOutcome>());
        Assert.Contains(AgentOutcome.Failed, Enum.GetValues<AgentOutcome>());
    }

    [Fact]
    public void A_cost_unknown_completion_reports_absence_rather_than_zero()
    {
        var agent = Spawned() with { Outcome = AgentOutcome.CompletedCostUnknown };

        Assert.Null(agent.TotalTokens);
        Assert.NotEqual(0, agent.TotalTokens ?? -1);
    }

    /// <summary>Enforced by the database: metrics may only accompany a priced completion.</summary>
    [Theory]
    [InlineData(AgentOutcome.CompletedCostUnknown, 1000L)]
    [InlineData(AgentOutcome.Running, 1000L)]
    [InlineData(AgentOutcome.Failed, 1000L)]
    public void Metrics_on_any_outcome_but_completed_are_refused(AgentOutcome outcome, long tokens)
    {
        using var temporary = new TemporaryStore();
        using var context = temporary.Store.Open();

        context.Agents.Add(Spawned() with { Outcome = outcome, TotalTokens = tokens });

        Assert.Throws<DbUpdateException>(() => context.SaveChanges());
    }

    [Fact]
    public void A_priced_completion_is_accepted()
    {
        using var temporary = new TemporaryStore();
        using var context = temporary.Store.Open();

        context.Agents.Add(Spawned() with
        {
            Outcome = AgentOutcome.Completed,
            TotalTokens = 1000,
            TotalToolCalls = 7,
            DurationMs = 4200,
            Model = "claude-opus-5",
        });
        context.SaveChanges();

        Assert.Equal(1000, context.Agents.Single().TotalTokens);
    }

    static Agent Spawned() => new()
    {
        SessionId = "session-1",
        AgentId = "call_42",
        SpawningToolCallId = "call_42",
        Name = "explore",
        DisplayName = "Explore",
        StartedAt = "2026-08-09T20:14:36.758Z",
        Outcome = AgentOutcome.Running,
    };
}
```

The store-level tests need the `agent` table, which arrives in Task 7 — expected, and recorded there.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/AecoPostMortem.Data.Tests/AecoPostMortem.Data.Tests.csproj --nologo -v q`

Expected: FAIL to compile — `AgentOutcome` and `Agent` do not exist.

- [ ] **Step 3: Write minimal implementation**

Create `src/AecoPostMortem.Data/Execution/Agent.cs`:

```csharp
namespace AecoPostMortem.Data.Execution;

/// <summary>
/// What became of a subagent. <see cref="CompletedCostUnknown"/> exists because
/// <c>subagent.completed</c> carries tokens and duration on only a measured 215 of 462 completions —
/// collapsing it into <see cref="Completed"/> with zeroes would price 247 agents at nothing.
/// </summary>
public enum AgentOutcome
{
    Running,
    Completed,
    CompletedCostUnknown,
    Failed,
}

/// <summary>
/// One subagent. Its handle is <c>subagent.started.data.toolCallId</c>, which the data map measured
/// identical to the <c>agentId</c> on every event the subagent produced.
/// </summary>
/// <remarks>
/// This entity carries no <see cref="IOwned"/>: it is the owner, and its key column is already the
/// agent id.
/// </remarks>
public sealed record Agent : IDerivedEntity
{
    public required string SessionId { get; init; }

    public required string AgentId { get; init; }

    /// <summary>The <c>task</c> call that produced it — a measured 470 of 470 spawns resolve.</summary>
    public required string SpawningToolCallId { get; init; }

    /// <summary>Derived from the <c>agentId</c> on the spawning call; a measured 178 of 470 are
    /// nested, so null means "spawned from the main thread" rather than "unknown".</summary>
    public string? ParentAgentId { get; init; }

    public required string Name { get; init; }

    public required string DisplayName { get; init; }

    public string? Description { get; init; }

    public required string StartedAt { get; init; }

    public required AgentOutcome Outcome { get; init; }

    public long? TotalTokens { get; init; }

    public int? TotalToolCalls { get; init; }

    public long? DurationMs { get; init; }

    public string? Model { get; init; }

    /// <summary>From <c>subagent.failed.data.error</c> — a measured 6 events across 2 sessions.</summary>
    public string? Error { get; init; }
}
```

Modify `src/AecoPostMortem.Data/PostMortemContext.cs`. Add the `DbSet`:

```csharp
    public DbSet<Agent> Agents => Set<Agent>();
```

Add the mapping and call `MapAgent(modelBuilder);` after `MapToolCall(modelBuilder);`:

```csharp
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
```

- [ ] **Step 4: Run tests to verify the model-level ones pass**

Run: `dotnet test test/AecoPostMortem.Data.Tests/AecoPostMortem.Data.Tests.csproj --nologo -v q --filter "FullyQualifiedName~AgentOutcomeTests.The_four_outcomes|FullyQualifiedName~AgentOutcomeTests.A_cost_unknown"`

Expected: PASS, 2 tests.

- [ ] **Step 5: Commit**

```bash
git add src/AecoPostMortem.Data/Execution/Agent.cs src/AecoPostMortem.Data/PostMortemContext.cs test/AecoPostMortem.Data.Tests/AgentOutcomeTests.cs
git commit -m "Give an agent that completed without a cost somewhere to say so (#12)" -m "Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_0124SRYTKGtQkfg4gfUeeQZq"
```

---

### Task 5: `Skill`, `Hook`, `Permission` and `WriteUnit`

Four small shapes that share a key form — `(SessionId, EventId)`, because Copilot writes no natural id for them — and change together. One file, one task.

**Files:**
- Create: `src/AecoPostMortem.Data/Execution/EventScopedEntities.cs`
- Modify: `src/AecoPostMortem.Data/PostMortemContext.cs`
- Test: `test/AecoPostMortem.Data.Tests/DerivedModelTests.cs`

**Interfaces:**
- Consumes: `IDerivedEntity`, `IOwned`, `MapOwnership`.
- Produces: `Skill`, `Hook`, `Permission`, `WriteUnit`, each keyed `(SessionId, EventId)`; `PostMortemContext.Skills`, `.Hooks`, `.Permissions`, `.WriteUnits`.

- [ ] **Step 1: Write the failing test**

Append to `test/AecoPostMortem.Data.Tests/DerivedModelTests.cs`:

```csharp
    /// <summary>Acceptance criterion 1, by name. Eight shapes, no more and no fewer — FileChange,
    /// RuleStatement and RuleSetVersion belong to S-07, S-19 and S-20.</summary>
    [Fact]
    public void The_eight_shapes_are_published()
    {
        using var context = new PostMortemContext();

        var published = context.Model.GetEntityTypes()
            .Where(type => typeof(IDerivedEntity).IsAssignableFrom(type.ClrType))
            .Select(type => type.ClrType.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            ["Agent", "Hook", "Permission", "Session", "Skill", "ToolCall", "Turn", "WriteUnit"],
            published);
    }

    /// <summary>Acceptance criterion 2, read from the model's key metadata rather than restated.</summary>
    [Fact]
    public void Every_derived_key_contains_the_session()
    {
        using var context = new PostMortemContext();

        var unscoped = context.Model.GetEntityTypes()
            .Where(type => typeof(IDerivedEntity).IsAssignableFrom(type.ClrType))
            .Where(type => !type.FindPrimaryKey()!.Properties
                .Any(property => property.GetColumnName() == "session_id"))
            .Select(type => type.ClrType.Name)
            .ToArray();

        Assert.True(
            unscoped.Length == 0,
            "Every entity is scoped by its session, and the session is part of its key: "
            + string.Join(", ", unscoped));
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/AecoPostMortem.Data.Tests/AecoPostMortem.Data.Tests.csproj --nologo -v q --filter "FullyQualifiedName~DerivedModelTests.The_eight_shapes"`

Expected: FAIL — four shapes published, eight expected.

- [ ] **Step 3: Write minimal implementation**

Create `src/AecoPostMortem.Data/Execution/EventScopedEntities.cs`:

```csharp
namespace AecoPostMortem.Data.Execution;

/// <summary>
/// One <c>skill.invoked</c> event — a measured 794 across 31 sessions, carrying a structured skill
/// boundary rather than a token-attributed inference.
/// </summary>
public sealed record Skill : IDerivedEntity, IOwned
{
    public required string SessionId { get; init; }

    /// <summary>The envelope <c>id</c>, measured present on 100% of events. Copilot writes no
    /// natural id for a skill invocation, so the event's own id is the local key.</summary>
    public required string EventId { get; init; }

    public required string Name { get; init; }

    public string? Path { get; init; }

    public string? Description { get; init; }

    public string? PluginName { get; init; }

    public string? PluginVersion { get; init; }

    public required string InvokedAt { get; init; }

    public required OwnerKind OwnerKind { get; init; }

    public string? AgentId { get; init; }
}

/// <summary>
/// One <c>hook.start</c> / <c>hook.end</c> pair. <see cref="Success"/> is a field rather than a
/// string match — a measured 35 failures across 3,027 pairs.
/// </summary>
public sealed record Hook : IDerivedEntity, IOwned
{
    public required string SessionId { get; init; }

    public required string EventId { get; init; }

    public required string Name { get; init; }

    public required string StartedAt { get; init; }

    public string? EndedAt { get; init; }

    public bool? Success { get; init; }

    public required OwnerKind OwnerKind { get; init; }

    public string? AgentId { get; init; }
}

/// <summary>
/// One permission request. <see cref="ResultKind"/> comes from
/// <c>permission.completed.data.result.kind</c>, an enum on Copilot rather than a string match — a
/// measured 1,033 requested against 1,031 completed, so an unanswered request is a real state.
/// </summary>
public sealed record Permission : IDerivedEntity, IOwned
{
    public required string SessionId { get; init; }

    public required string EventId { get; init; }

    public required string RequestedAt { get; init; }

    public string? CompletedAt { get; init; }

    public string? ResultKind { get; init; }

    public string? ToolCallId { get; init; }

    public required OwnerKind OwnerKind { get; init; }

    public string? AgentId { get; init; }
}

/// <summary>
/// A unit of content the agent wrote. Published and never populated in v1: FR-36 is Phase E, gated
/// out by PRD §3.4.3. The shape exists so the stories that will consume it have something to
/// compile against.
/// </summary>
public sealed record WriteUnit : IDerivedEntity, IOwned
{
    public required string SessionId { get; init; }

    public required string EventId { get; init; }

    public required string ToolCallId { get; init; }

    public required string Path { get; init; }

    public required string AddedContent { get; init; }

    public required OwnerKind OwnerKind { get; init; }

    public string? AgentId { get; init; }
}
```

Modify `src/AecoPostMortem.Data/PostMortemContext.cs`. Add the four `DbSet`s:

```csharp
    public DbSet<Skill> Skills => Set<Skill>();

    public DbSet<Hook> Hooks => Set<Hook>();

    public DbSet<Permission> Permissions => Set<Permission>();

    public DbSet<WriteUnit> WriteUnits => Set<WriteUnit>();
```

Add the mapping and call `MapEventScopedEntities(modelBuilder);` after `MapAgent(modelBuilder);`:

```csharp
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
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test test/AecoPostMortem.Data.Tests/AecoPostMortem.Data.Tests.csproj --nologo -v q --filter "FullyQualifiedName~DerivedModelTests"`

Expected: PASS, 9 tests — including `The_eight_shapes_are_published` and `Every_derived_key_contains_the_session`.

- [ ] **Step 5: Commit**

```bash
git add src/AecoPostMortem.Data/Execution/EventScopedEntities.cs src/AecoPostMortem.Data/PostMortemContext.cs test/AecoPostMortem.Data.Tests/DerivedModelTests.cs
git commit -m "Publish the four event-keyed shapes, and the eight are complete (#12)" -m "Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_0124SRYTKGtQkfg4gfUeeQZq"
```

---

### Task 6: `store_metadata`, its migration, and the widened guard

This is the one task that touches `Migrations/`. `store_metadata` records the store's own state, so it is not re-derivable and it is migrated. Widening the S-01 guard is part of the same commit, with the reason in the test.

**Files:**
- Create: `src/AecoPostMortem.Data/StoreMetadata.cs`
- Create: `src/AecoPostMortem.Data/Migrations/<timestamp>_AddStoreMetadata.cs` (generated)
- Modify: `src/AecoPostMortem.Data/PostMortemContext.cs`
- Modify: `test/AecoPostMortem.Data.Tests/SchemaTests.cs:47-60`

**Interfaces:**
- Consumes: nothing new.
- Produces: `StoreMetadata` with `required string Key` / `required string Value`; the constant `StoreMetadata.DerivedSchemaVersionKey = "derived_schema_version"`; `PostMortemContext.StoreMetadata` as `DbSet<StoreMetadata>`.

- [ ] **Step 1: Write the failing test**

Modify `test/AecoPostMortem.Data.Tests/SchemaTests.cs`. Replace the body of `RAW_is_the_only_table_the_migrations_create` and rename it:

```csharp
    [Fact]
    public void The_migrations_create_only_RAW_and_the_stores_own_metadata()
    {
        // Repo Rule 4 / PRD §3.8: NORMALIZED and FINDINGS are re-derived from RAW, never migrated.
        // A third table appearing here means a migration was authored against a derived layer.
        //
        // store_metadata is migrated deliberately and is not a derived layer: it records the
        // store's own state, including the derived schema's version, and a value dropped alongside
        // the tables it describes could not be compared against them.
        using var temporary = new TemporaryStore();
        using var context = temporary.Store.Open();

        var tables = Query(
                context,
                "SELECT name FROM sqlite_master WHERE type = 'table' "
                + "AND name NOT LIKE 'sqlite_%' AND name NOT LIKE '\\_\\_EF%' ESCAPE '\\' "
                + "ORDER BY name")
            .ToArray();

        Assert.Equal(["raw_event", "store_metadata"], tables);
    }
```

Note: this test opens a store, and `Open()` will create the derived tables once Task 7 lands — so the query must exclude them. Task 7's Step 3 adds that exclusion; until then this test sees only the two migrated tables and passes.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/AecoPostMortem.Data.Tests/AecoPostMortem.Data.Tests.csproj --nologo -v q --filter "FullyQualifiedName~SchemaTests"`

Expected: FAIL — `store_metadata` does not exist, so the query returns `raw_event` alone.

- [ ] **Step 3: Write minimal implementation**

Create `src/AecoPostMortem.Data/StoreMetadata.cs`:

```csharp
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
```

Modify `src/AecoPostMortem.Data/PostMortemContext.cs`. Add the `DbSet`:

```csharp
    public DbSet<StoreMetadata> StoreMetadata => Set<StoreMetadata>();
```

Add the mapping, and call `MapStoreMetadata(modelBuilder);` in `OnModelCreating` immediately after the `rawEvent` block and before `MapSession(modelBuilder);`:

```csharp
    static void MapStoreMetadata(ModelBuilder modelBuilder)
    {
        var metadata = modelBuilder.Entity<StoreMetadata>();

        metadata.ToTable("store_metadata");
        metadata.HasKey(row => row.Key);

        metadata.Property(row => row.Key).HasColumnName("key");
        metadata.Property(row => row.Value).HasColumnName("value");
    }
```

Generate the migration:

```bash
dotnet ef migrations add AddStoreMetadata --project src/AecoPostMortem.Data --output-dir Migrations
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test test/AecoPostMortem.Data.Tests/AecoPostMortem.Data.Tests.csproj --nologo -v q`

Expected: PASS for every `SchemaTests` and `StoreCreationTests` test. `StoreUpgradeTests.A_store_at_an_earlier_migration_is_brought_forward_with_its_rows_intact` now has two migrations to work with and exercises its real path for the first time — confirm it passes, since that is the case it was written to catch.

- [ ] **Step 5: Commit**

```bash
git add src/AecoPostMortem.Data/StoreMetadata.cs src/AecoPostMortem.Data/PostMortemContext.cs src/AecoPostMortem.Data/Migrations test/AecoPostMortem.Data.Tests/SchemaTests.cs
git commit -m "Give the store somewhere to record its own state (#12)" -m "Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_0124SRYTKGtQkfg4gfUeeQZq"
```

---

### Task 7: `DerivedSchema` — create, drop, and the computed version

This is the task that makes the derived tables exist. It also turns on the store-level tests written in Tasks 2 and 4.

**Files:**
- Create: `src/AecoPostMortem.Data/Execution/DerivedSchema.cs`
- Modify: `src/AecoPostMortem.Data/LocalStore.cs`
- Modify: `test/AecoPostMortem.Data.Tests/SchemaTests.cs`
- Test: `test/AecoPostMortem.Data.Tests/DerivedSchemaTests.cs`

**Interfaces:**
- Consumes: `IDerivedEntity`, all eight entities, `StoreMetadata.DerivedSchemaVersionKey`.
- Produces: `DerivedSchema.CreateStatements(PostMortemContext)` → `IReadOnlyList<string>`; `DerivedSchema.DropStatements(PostMortemContext)` → `IReadOnlyList<string>`; `DerivedSchema.Version(PostMortemContext)` → `string`; `DerivedSchema.Create(PostMortemContext)`; `DerivedSchema.Drop(PostMortemContext)`; `DerivedSchema.EnsureCurrent(PostMortemContext)`.

**Why the DDL is generated by hand rather than by EF.** `ExcludeFromMigrations` makes `IMigrationsModelDiffer` skip those tables — the same flag that keeps them out of migrations keeps them out of every EF-generated script, including `GenerateCreateScript()`. So the statements are built from the model's own metadata. That is not a workaround: it is what makes the version hash a hash of exactly the DDL that runs.

- [ ] **Step 1: Write the failing test**

Create `test/AecoPostMortem.Data.Tests/DerivedSchemaTests.cs`:

```csharp
using AecoPostMortem.Data.Execution;
using Microsoft.EntityFrameworkCore;

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

    static string[] DerivedTables(PostMortemContext context)
    {
        var derived = context.Model.GetEntityTypes()
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
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/AecoPostMortem.Data.Tests/AecoPostMortem.Data.Tests.csproj --nologo -v q --filter "FullyQualifiedName~DerivedSchemaTests"`

Expected: FAIL to compile — `DerivedSchema` does not exist.

- [ ] **Step 3: Write minimal implementation**

Create `src/AecoPostMortem.Data/Execution/DerivedSchema.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
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
        context.Model.GetEntityTypes()
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
```

Modify `src/AecoPostMortem.Data/LocalStore.cs`. Add `using AecoPostMortem.Data.Execution;` and, in `Open()`, insert the call between `Migrate()` and the permission call:

```csharp
        var context = new PostMortemContext(Options());
        context.Database.Migrate();
        DerivedSchema.EnsureCurrent(context);

        OwnerOnlyAccess.ApplyToFile(FilePath);
```

Update the doc comment on `Open()` to add a sentence:

```csharp
    /// The derived tables are created from the model at the same time, and recreated when the
    /// model's version moves — they are re-derivable from RAW, so they are rebuilt rather than
    /// migrated (PRD §3.8).
```

Modify `test/AecoPostMortem.Data.Tests/SchemaTests.cs` — `The_migrations_create_only_RAW_and_the_stores_own_metadata` now sees the derived tables too, so exclude them. Replace its query with:

```csharp
        var derived = context.Model.GetEntityTypes()
            .Where(type => typeof(AecoPostMortem.Data.Execution.IDerivedEntity).IsAssignableFrom(type.ClrType))
            .Select(type => type.GetTableName()!)
            .ToHashSet(StringComparer.Ordinal);

        var tables = Query(
                context,
                "SELECT name FROM sqlite_master WHERE type = 'table' "
                + "AND name NOT LIKE 'sqlite_%' AND name NOT LIKE '\\_\\_EF%' ESCAPE '\\' "
                + "ORDER BY name")
            .Where(name => !derived.Contains(name))
            .ToArray();

        Assert.Equal(["raw_event", "store_metadata"], tables);
```

- [ ] **Step 4: Run the full suite**

Run: `dotnet test AecoPostMortem.sln --nologo -v q`

Expected: PASS everywhere. Specifically, the store-level tests deferred from earlier tasks now run for the first time — `OwnershipTests.A_mismatched_pair_is_refused_by_the_store`, `OwnershipTests.A_matched_pair_is_accepted`, `AgentOutcomeTests.Metrics_on_any_outcome_but_completed_are_refused` and `AgentOutcomeTests.A_priced_completion_is_accepted`. If the check constraints do not fire, `HasCheckConstraint` is not reaching the generated DDL — verify by printing `DerivedSchema.CreateStatements` and looking for the `CONSTRAINT` lines.

- [ ] **Step 5: Commit**

```bash
git add src/AecoPostMortem.Data/Execution/DerivedSchema.cs src/AecoPostMortem.Data/LocalStore.cs test/AecoPostMortem.Data.Tests/DerivedSchemaTests.cs test/AecoPostMortem.Data.Tests/SchemaTests.cs
git commit -m "Create the derived tables from the model, and version them by their own DDL (#12)" -m "Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_0124SRYTKGtQkfg4gfUeeQZq"
```

---

### Task 8: The documentation the contract is read from

The acceptance criterion is "given the contract, when it is **read**". The types are half of that; the other half is the document a downstream story reads before writing a query.

**Files:**
- Modify: `docs/claude/DOMAIN_MODEL.md`
- Modify: `src/AecoPostMortem.Data/CLAUDE.md`

**Interfaces:**
- Consumes: everything built in Tasks 1–7.
- Produces: no code.

- [ ] **Step 1: Add the eight entities to the domain model**

In `docs/claude/DOMAIN_MODEL.md`, after the `RawEvent` section, add a `## NORMALIZED` section containing:

- an opening paragraph stating that these eight are re-derived from RAW, never migrated, and that their tables are created from the model and versioned by a hash of their own DDL;
- one `###` subsection per entity, each with its table name, its key, and a property table in the same shape as `RawEvent`'s — property, type, DB column, notes with the measured coverage that made a column nullable;
- an `### Invariants` subsection carrying the three from the spec: session-scoped natural keys, ownership as a value with `owner_kind` NOT NULL, and the agent completion tri-state with its check constraint;
- a note that message text is not here and is read from `raw_event`, and that `FileChange`, `RuleStatement` and `RuleSetVersion` belong to S-07, S-19 and S-20.

Every figure quoted must carry `measured` on the same line, or `scripts/check-claims.py` fails.

- [ ] **Step 2: Run the checkers**

Run:
```bash
python scripts/check-claims.py docs/claude/DOMAIN_MODEL.md
python scripts/check-claude-md.py docs/claude/DOMAIN_MODEL.md
```
Expected: no unsourced claims; no findings. `DOMAIN_MODEL.md` is a sidecar, so it must stay within 200 lines and 14,000 bytes — if it does not, that is a signal to split, not to trim the coverage notes.

- [ ] **Step 3: Update the Data router**

In `src/AecoPostMortem.Data/CLAUDE.md`:

- add the `Execution/` files to the Structure table;
- add a `### The derived layer is created from the model, not from a migration` decision, stating that `ExcludeFromMigrations` is applied by a loop over `IDerivedEntity`, that it also hides these tables from EF's own script generation, and that this is why `DerivedSchema` builds the DDL itself;
- add a `### The derived schema's version is a hash of its own DDL` decision, stating that a mismatch drops and recreates the tables and that the rows go with them because they are re-derivable;
- add a `## Playbook — adding a derived entity` section: implement `IDerivedEntity` (and `IOwned` if a subagent can own it); map it in `OnModelCreating` with snake_case columns; add it to `DerivedModelTests.The_eight_shapes_are_published`; no migration.

Rule 6 forbids changelog voice in a router — no `S-nn`, no `#nn`, no "previously" or "no longer".

- [ ] **Step 4: Run the checker**

Run: `python scripts/check-claude-md.py src/AecoPostMortem.Data`
Expected: `All migrated files pass`.

- [ ] **Step 5: Run the full suite and commit**

```bash
dotnet test AecoPostMortem.sln --nologo -v q
git add docs/claude/DOMAIN_MODEL.md src/AecoPostMortem.Data/CLAUDE.md
git commit -m "Write down the contract the fifteen stories will read (#12)" -m "Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_0124SRYTKGtQkfg4gfUeeQZq"
```

---

## Self-Review

**Spec coverage.** §2 (one context, exclusion by convention) → Task 1. §3.1 (session-scoped natural keys) → the keys in Tasks 1–5, asserted in Task 5. §3.2 (ownership) → Task 2. §3.3 (agent tri-state) → Task 4. §4 (the eight shapes) → Tasks 1–5, asserted in Task 5. §5 (indexes) → Task 3 for `tool_call`, Tasks 2, 4 and 5 for the rest. §6 (schema version, `store_metadata`, the widened S-01 guard) → Tasks 6 and 7. §7 (what ships) → all tasks; the "does not ship" list is honoured — nothing populates a table. §8 (the nine tests) → all nine appear, distributed to the task that makes each meaningful.

**One spec deviation, stated rather than absorbed.** §3.2's C# surfacing becomes `IOwned` plus two properties instead of a value type mapped as a complex property; the reason is in Global Constraints. The invariant and the DDL are unchanged.

**Type consistency.** `IDerivedEntity` (Task 1) is the type Tasks 3–5 and 7 filter on. `IOwned` and `MapOwnership` (Task 2) are used by Tasks 3 and 5 with the same signature. `StoreMetadata.DerivedSchemaVersionKey` (Task 6) is read by Task 7. `DerivedSchema.Version` / `.Create` / `.Drop` / `.EnsureCurrent` (Task 7) are named identically in `LocalStore` and in `DerivedSchemaTests`. `TurnOutcome` and `AgentOutcome` are stored via `HasConversion<string>()`, so the `ck_agent_cost` constraint compares against `'Completed'` — the enum member name, not a lower-cased form — and Task 4's constraint SQL matches.

**Ordering risk, accepted deliberately.** Tasks 2 and 4 each leave store-level tests failing until Task 7 creates the tables. The alternative was building `DerivedSchema` before any entity existed, which would have made it untestable. Each task's Step 4 names exactly which tests must pass at that point, so a reviewer gating Task 2 is not looking at a red suite by accident.
