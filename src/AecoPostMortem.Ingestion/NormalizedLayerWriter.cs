using AecoPostMortem.Data;
using AecoPostMortem.Data.Execution;
using Microsoft.EntityFrameworkCore;

namespace AecoPostMortem.Ingestion;

/// <summary>
/// Derives one session's NORMALIZED rows from its own RAW events and writes them, replacing
/// whatever was there before — never patched incrementally, the same "re-derived from RAW, not
/// migrated" discipline the rest of the derived layer follows. Ties together
/// <see cref="SessionBuilder"/>, <see cref="ExecutionRecordBuilder"/>, <see cref="SkillBuilder"/>,
/// <see cref="HookBuilder"/> and <see cref="PermissionBuilder"/>, which is what <c>ingest</c> and
/// <c>rebuild</c> both call instead of leaving the seven tables empty.
/// <see cref="AecoPostMortem.Data.Execution.WriteUnit"/> is the one table still deliberately not
/// written here: it stays empty until Phase E regardless (FR-36, PRD §3.4.3).
/// </summary>
public static class NormalizedLayerWriter
{
    /// <summary>
    /// Reads <paramref name="sessionId"/>'s own RAW events (ordered by <see cref="RawEvent.Sequence"/>,
    /// the same ordering every other builder in this project already trusts), deletes whatever this
    /// session already carries in the six derived tables, and writes the freshly built rows.
    /// A session whose first event is not <c>session.start</c> — <see cref="SessionBuilder.Build"/>
    /// returns <see langword="null"/> — is left with no rows at all: <c>GetSession</c> 404s on a
    /// missing <see cref="AecoPostMortem.Data.Execution.Session"/> row before it ever reads the other
    /// five tables, so writing orphaned turns or tool calls for it would be dead weight no caller
    /// could reach.
    /// </summary>
    public static void Derive(PostMortemContext context, string sessionId)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        DeleteForSession(context, sessionId);

        var events = context.RawEvents
            .Where(raw => raw.SessionId == sessionId)
            .OrderBy(raw => raw.Sequence)
            .ToList();

        var session = SessionBuilder.Build(sessionId, events);
        if (session is null)
        {
            return;
        }

        var record = ExecutionRecordBuilder.Build(sessionId, events);
        var skills = SkillBuilder.Build(sessionId, events);
        var hooks = HookBuilder.Build(sessionId, events);
        var permissions = PermissionBuilder.Build(sessionId, events);

        context.Sessions.Add(session);
        context.Turns.AddRange(record.Turns);
        context.ToolCalls.AddRange(record.ToolCalls);
        context.Agents.AddRange(record.Agents);
        context.Skills.AddRange(skills);
        context.Hooks.AddRange(hooks);
        context.Permissions.AddRange(permissions);

        context.SaveChanges();
    }

    /// <summary>
    /// Removes <paramref name="sessionId"/>'s rows from all seven derived tables this writer owns,
    /// via bulk <c>ExecuteDelete</c> the same way <see cref="RawEventBatch.DeleteBySession"/> purges
    /// RAW for a retroactively excluded session — no per-entity tracking cost for a delete this
    /// shaped. Exposed separately from <see cref="Derive"/> so a session excluded after already being
    /// ingested can have its NORMALIZED rows purged too, not only its RAW ones (FR-7's "no event from
    /// that session is persisted" has to hold for the derived layer as well, or the Flight Recorder
    /// would still show a session the operator asked to exclude).
    /// </summary>
    public static void DeleteForSession(PostMortemContext context, string sessionId)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        context.Sessions.Where(row => row.SessionId == sessionId).ExecuteDelete();
        context.Turns.Where(row => row.SessionId == sessionId).ExecuteDelete();
        context.ToolCalls.Where(row => row.SessionId == sessionId).ExecuteDelete();
        context.Agents.Where(row => row.SessionId == sessionId).ExecuteDelete();
        context.Skills.Where(row => row.SessionId == sessionId).ExecuteDelete();
        context.Hooks.Where(row => row.SessionId == sessionId).ExecuteDelete();
        context.Permissions.Where(row => row.SessionId == sessionId).ExecuteDelete();

        // ExecuteDelete issues SQL directly and never touches the change tracker (the same reason
        // RawEventBatch's own append bypasses it) — a row this same context added and saved earlier
        // in its lifetime is still tracked as Unchanged after the DELETE runs underneath it, and a
        // later Add with the same key would collide with that stale entry. Clearing here is what
        // makes deriving the same session twice in one context (Derive calls this first) replace
        // rather than throw.
        context.ChangeTracker.Clear();
    }

    /// <summary>
    /// "Rebuild", as one definition both callers share: drop and recreate the derived schema
    /// (<see cref="DerivedSchema.Rebuild"/>, unconditional — distinct from the version-gated rebuild
    /// <see cref="LocalStore.Open"/> already runs via <c>EnsureCurrent</c>), then <see cref="Derive"/>
    /// every distinct session RAW still holds. Before this method existed, <c>AecoPostMortem.Cli</c>'s
    /// <c>rebuild</c> command carried this exact sequence inline; <c>AecoPostMortem.Api</c>'s
    /// <c>POST /api/rebuild</c> needs the identical sequence, and factoring it here — the layer both
    /// projects already reference — means "what rebuild means" is defined in exactly one place rather
    /// than two thin dispatch layers each repeating the loop. Returns the session ids re-derived, so a
    /// caller can report how many without a second query of its own.
    /// </summary>
    public static IReadOnlyList<string> RebuildAll(PostMortemContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        DerivedSchema.Rebuild(context);

        var sessionIds = context.RawEvents.Select(raw => raw.SessionId).Distinct().ToList();
        foreach (var sessionId in sessionIds)
        {
            Derive(context, sessionId);
        }

        return sessionIds;
    }
}
