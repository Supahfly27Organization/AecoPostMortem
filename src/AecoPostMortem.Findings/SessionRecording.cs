using System.Globalization;
using AecoPostMortem.Data.Execution;

namespace AecoPostMortem.Findings;

/// <summary>
/// FR-21, part 1 of 3 (S-08, issue #15): one step on the Flight Recorder's tape. The five kinds
/// name every step shape the story's own Gherkin names — "hooks, prompts, skills, tool calls and
/// MCP calls" — with <see cref="Prompt"/> standing for one assistant turn (Copilot writes no
/// <c>Turn</c>-shaped "prompt" entity of its own; a turn is the bounded shape a prompt/response
/// cycle has, and the operator's own prompt text is a separate <c>user.message</c> event this layer
/// has no RAW access to — resolved at the <c>Api</c> layer by <c>PromptTextLookup</c>) and
/// <see cref="McpCall"/> a <see cref="ToolCall"/> that names an MCP server, kept distinct
/// from a plain <see cref="ToolCall"/> rather than folded into it.
/// </summary>
public enum SessionTapeStepKind
{
    Prompt,
    Hook,
    Skill,
    ToolCall,
    McpCall,
}

/// <summary>
/// One tape entry. <see cref="StepId"/> is the underlying entity's own key within its session — a
/// <see cref="Turn.EventId"/>, a <see cref="ToolCall.ToolCallId"/>, or an event-scoped
/// <c>EventId</c> — never a display field. For a <see cref="SessionTapeStepKind.Prompt"/> step that
/// distinction is load-bearing rather than pedantic: <see cref="Turn.TurnId"/> is Copilot's own
/// cycling display counter and repeats within a single session (measured on 20 of 25 real sessions
/// in the dominant repository of the live reference corpus; the worst case collapsed 310 real prompt
/// steps onto 73 distinct ids), which is exactly why <c>Data.Execution.Turn</c> is itself keyed by
/// <see cref="Turn.EventId"/> (<c>AecoPostMortem.Data/CLAUDE.md</c>). Every consumer that resolves a
/// step back to its own RAW event — <c>Api.StepEvidenceLookup</c>, <c>Api.PromptTextLookup</c> — and
/// every client that addresses one (<c>GET /api/sessions/{id}/steps/{stepId}</c>, the tape's own DOM
/// ids) depends on this being collision-free within a session.
/// <see cref="Label"/> has no message text on it: <c>Turn</c> carries none
/// (<c>AecoPostMortem.Data/CLAUDE.md</c> — "messages are read from RAW"), so a prompt step is
/// labelled by its outcome; the operator's real prompt text is resolved one layer out, where RAW is
/// reachable (<c>Api.PromptTextLookup</c>).
/// </summary>
public sealed record SessionTapeStep
{
    public required SessionTapeStepKind Kind { get; init; }

    public required string StepId { get; init; }

    public required string Label { get; init; }

    /// <summary>FR-25 (S-12, issue #21): the plugin a <see cref="SessionTapeStepKind.Skill"/> step
    /// was invoked from, carried alongside <see cref="Label"/> (the skill's own name) rather than
    /// folded into it. <see langword="null"/> for every other step kind, and for a skill Copilot
    /// recorded no plugin for.</summary>
    public string? PluginName { get; init; }

    /// <summary>The plugin's version, paired with <see cref="PluginName"/> — never populated
    /// without it, matching <see cref="Data.Execution.Skill.PluginVersion"/>'s own nullability.</summary>
    public string? PluginVersion { get; init; }

    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>How long after the session's own <see cref="Session.StartedAt"/> this step began —
    /// Scenario 2's "offset from session start". Not expected to be negative — every step should
    /// start at or after its own session — but nothing here guards against a malformed row whose
    /// timestamp precedes <see cref="Session.StartedAt"/>; a negative value would mean exactly
    /// that, and is passed through rather than clamped.</summary>
    public required TimeSpan Offset { get; init; }

    public required OwnerKind OwnerKind { get; init; }

    public string? AgentId { get; init; }
}

/// <summary>
/// The tape: every step in wall-clock order. <see cref="HasSteps"/> is a computed property, not a
/// second stored flag that could drift from <see cref="Steps"/> — an empty list already states
/// Scenario 3's "no steps were recorded" on its own; this exists only so a caller does not have to
/// re-derive the check from <c>Steps.Count</c> itself.
/// </summary>
public sealed record SessionTape
{
    public required IReadOnlyList<SessionTapeStep> Steps { get; init; }

    public bool HasSteps => Steps.Count > 0;
}

/// <summary>
/// FR-21's session masthead (Scenario 1): identity, repository, branch, CLI version, elapsed time,
/// and the five step-population counts, plus context size at end. <see cref="Elapsed"/> is
/// <see langword="null"/>, never zero-filled, when the session never wrote <c>session.shutdown</c>
/// (<see cref="Session.EndedAt"/> is <see langword="null"/>) — the same "a zero is a number a
/// surface would print" discipline <see cref="SessionTokenFigures"/> already documents for its own
/// totals. <see cref="ModelCount"/> reuses <see cref="Session.ModelCount"/> verbatim rather than
/// inventing a second "models" figure: NORMALIZED carries no main-thread model field today (only
/// <see cref="Agent.Model"/>, subagent-scoped), so the count already summed into
/// <see cref="ContextSize"/>'s totals is the one "models" figure this layer can state honestly.
/// </summary>
public sealed record SessionMasthead
{
    public required string SessionId { get; init; }

    public string? Repository { get; init; }

    public string? Branch { get; init; }

    public required string CopilotVersion { get; init; }

    /// <summary>Mockup parity item #14: the session's own wall-clock start, so a masthead can show
    /// a real start→end range alongside <see cref="Elapsed"/>'s duration — <see cref="Session.
    /// StartedAt"/> parsed the same way <see cref="Elapsed"/> already is, never a second source of
    /// truth.</summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary><see langword="null"/> under the identical condition <see cref="Elapsed"/> is —
    /// <see cref="Session.EndedAt"/> was never recorded (no <c>session.shutdown</c>) — never
    /// zero-filled or defaulted to "now", the same "a zero is a number a surface would print"
    /// discipline this record's own doc comment already states for <see cref="Elapsed"/>.</summary>
    public DateTimeOffset? EndedAt { get; init; }

    public TimeSpan? Elapsed { get; init; }

    public required int TurnCount { get; init; }

    public required int ToolCallCount { get; init; }

    public required int SubagentCount { get; init; }

    public required int SkillCount { get; init; }

    public int? ModelCount { get; init; }

    public required SessionTokenFigures ContextSize { get; init; }
}

/// <summary>
/// FR-21, part 3 of 3 (S-53, issue #17): whether a <see cref="SessionRecording"/> is ready to be
/// read as final. A closed union, the same discipline <see cref="SessionTokenFigures"/> already uses
/// for its own two shapes — "final" is not a boolean a caller could forget to check:
/// <see cref="Complete"/> is the only shape a UI may render the tape from without also rendering a
/// caveat above it.
/// </summary>
public abstract record SessionRecordingStatus
{
    private SessionRecordingStatus()
    {
    }

    /// <summary>The one value for "nothing here is provisional".</summary>
    public static SessionRecordingStatus CompleteValue { get; } = new Complete();

    /// <summary>The one value for Scenario 3 — no per-instance data needed, the same reasoning
    /// <see cref="SessionTokenFigures.SessionTotalsNotRecorded"/> gives its own singleton.</summary>
    public static SessionRecordingStatus IngestIncompleteValue { get; } = new IngestIncomplete();

    public sealed record Complete : SessionRecordingStatus;

    /// <summary>Scenario 3: the session has not recorded its own end. Nothing about its captured
    /// lifecycle has concluded, so today's masthead and tape figures are not the final ones this
    /// session will eventually have — the recorder states that rather than presenting what has
    /// arrived so far as though it were the whole session.</summary>
    public sealed record IngestIncomplete : SessionRecordingStatus;

    /// <summary>Scenario 4: reconstruction found something it could not resolve.
    /// <see cref="Skipped"/> states what, in the operator's own words — never a bare count with no
    /// explanation, the same "never a percentage without the count that produced it" discipline this
    /// project applies to a Waste finding's rate (see this project's own CLAUDE.md).</summary>
    public sealed record ReconstructionFailed : SessionRecordingStatus
    {
        public required IReadOnlyList<string> Skipped { get; init; }
    }
}

/// <summary>
/// FR-21's masthead and tape (S-08, issue #15) — "the half everything else hangs off": S-52
/// (inspector, finding chips) and S-53 (scale, states) both attach to <see cref="Tape"/>, which is
/// why this story stops at the tape itself rather than reaching for either.
/// </summary>
public sealed record SessionRecording
{
    public required SessionMasthead Masthead { get; init; }

    public required SessionTape Tape { get; init; }

    /// <summary>FR-21 part 3 of 3 (S-53, issue #17). Defaults to <see cref="SessionRecordingStatus.
    /// Complete"/> only in the sense that a session with a recorded end and no reconstruction
    /// problem reaches that value through <see cref="DetermineStatus"/> — this field is never left
    /// unset.</summary>
    public required SessionRecordingStatus Status { get; init; }

    /// <summary>
    /// Builds a session's masthead and tape from plain, already-resolved <c>Data.Execution</c>
    /// rows — the same reasoning <c>HookFailureFinding.Build</c> and <c>AbortedTurnFinding.Build</c>
    /// give for taking plain inputs rather than reading through <c>PostMortemContext</c> directly:
    /// nothing in this project may query the store on its own, and the caller (here,
    /// <c>AecoPostMortem.Api</c>) is the one that knows where its rows came from.
    /// </summary>
    /// <param name="spawnResolution">FR-9's own reconstruction check (<c>Ingestion.
    /// SpawnResolutionCheck</c>), already resolved by the caller — the same already-resolved-input
    /// pattern <c>MastheadCounters</c> establishes. <see langword="null"/> means no reconstruction
    /// check was run for this session, which reads as <see cref="SessionRecordingStatus.Complete"/>,
    /// never as a failure this method invented on its own.</param>
    public static SessionRecording Build(
        Session session,
        IReadOnlyList<Turn> turns,
        IReadOnlyList<ToolCall> toolCalls,
        IReadOnlyList<Agent> agents,
        IReadOnlyList<Skill> skills,
        IReadOnlyList<Hook> hooks,
        CheckRegistryEntry? spawnResolution = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(turns);
        ArgumentNullException.ThrowIfNull(toolCalls);
        ArgumentNullException.ThrowIfNull(agents);
        ArgumentNullException.ThrowIfNull(skills);
        ArgumentNullException.ThrowIfNull(hooks);

        var start = ParseTimestamp(session.StartedAt);

        var masthead = new SessionMasthead
        {
            SessionId = session.SessionId,
            Repository = session.Repository,
            Branch = session.Branch,
            CopilotVersion = session.CopilotVersion,
            StartedAt = start,
            EndedAt = session.EndedAt is { } endedAtForMasthead ? ParseTimestamp(endedAtForMasthead) : null,
            Elapsed = session.EndedAt is { } endedAt ? ParseTimestamp(endedAt) - start : null,
            TurnCount = turns.Count,
            ToolCallCount = toolCalls.Count,
            SubagentCount = agents.Count,
            SkillCount = skills.Count,
            ModelCount = session.ModelCount,
            ContextSize = SessionTokenFigures.From(session),
        };

        var steps = new List<SessionTapeStep>(turns.Count + toolCalls.Count + skills.Count + hooks.Count);

        foreach (var turn in turns)
        {
            // `Turn.EventId`, never `Turn.TurnId`: the latter is Copilot's own cycling display
            // counter, not an identity (`AecoPostMortem.Data/CLAUDE.md`, "`Turn` is keyed by its own
            // event id"). See this type's own remarks on `StepId` for the measured collision.
            steps.Add(BuildStep(
                SessionTapeStepKind.Prompt, turn.EventId, turn.Outcome.ToString(), turn.StartedAt,
                start, turn.OwnerKind, turn.AgentId));
        }

        foreach (var toolCall in toolCalls)
        {
            var kind = toolCall.McpServerName is not null ? SessionTapeStepKind.McpCall : SessionTapeStepKind.ToolCall;
            steps.Add(BuildStep(
                kind, toolCall.ToolCallId, toolCall.ToolName, toolCall.StartedAt,
                start, toolCall.OwnerKind, toolCall.AgentId));
        }

        foreach (var skill in skills)
        {
            steps.Add(BuildStep(
                SessionTapeStepKind.Skill, skill.EventId, skill.Name, skill.InvokedAt,
                start, skill.OwnerKind, skill.AgentId, skill.PluginName, skill.PluginVersion));
        }

        foreach (var hook in hooks)
        {
            steps.Add(BuildStep(
                SessionTapeStepKind.Hook, hook.EventId, hook.Name, hook.StartedAt,
                start, hook.OwnerKind, hook.AgentId));
        }

        // Ordered by wall-clock time (Scenario 2); ties broken deterministically (PRD §3.8) by
        // step kind, then the step's own id, since two entities can share one timestamp but never
        // one (kind, id) pair within a session.
        var ordered = steps
            .OrderBy(step => step.Timestamp)
            .ThenBy(step => step.Kind)
            .ThenBy(step => step.StepId, StringComparer.Ordinal)
            .ToArray();

        return new SessionRecording
        {
            Masthead = masthead,
            Tape = new SessionTape { Steps = ordered },
            Status = DetermineStatus(session, spawnResolution),
        };
    }

    /// <summary>Scenario 3 is checked first — the more urgent, more specific claim wins, the same
    /// ordering <c>ProcessDigest.Build</c> gives <c>MastheadCounters.IngestInProgress</c> over its
    /// own analysis-state check (<c>Findings/CLAUDE.md</c>): while the session itself has not
    /// concluded, nothing here can be trusted as final, not even a reconstruction diagnosis over
    /// whatever partial data has arrived so far.</summary>
    static SessionRecordingStatus DetermineStatus(Session session, CheckRegistryEntry? spawnResolution)
    {
        if (session.EndedAt is null)
        {
            return SessionRecordingStatus.IngestIncompleteValue;
        }

        if (spawnResolution is { FindingCount: > 0 } check)
        {
            return new SessionRecordingStatus.ReconstructionFailed
            {
                Skipped =
                [
                    $"{check.FindingCount} of {check.Population} subagent spawn(s) could not be resolved to their originating tool call",
                ],
            };
        }

        return SessionRecordingStatus.CompleteValue;
    }

    static SessionTapeStep BuildStep(
        SessionTapeStepKind kind,
        string stepId,
        string label,
        string timestamp,
        DateTimeOffset sessionStart,
        OwnerKind ownerKind,
        string? agentId,
        string? pluginName = null,
        string? pluginVersion = null)
    {
        var parsed = ParseTimestamp(timestamp);

        return new SessionTapeStep
        {
            Kind = kind,
            StepId = stepId,
            Label = label,
            PluginName = pluginName,
            PluginVersion = pluginVersion,
            Timestamp = parsed,
            Offset = parsed - sessionStart,
            OwnerKind = ownerKind,
            AgentId = agentId,
        };
    }

    /// <summary>
    /// Deliberately fail-loud, the same "decode or throw" philosophy
    /// <c>AecoPostMortem.Data.RawPayload.FromUtf8</c> already applies at the RAW boundary: every
    /// timestamp reaching here came from a <c>Data.Execution</c> row this project trusts to already
    /// be well-formed (a future ETL writer's job to guarantee, not this method's to defend against
    /// silently), so a malformed value is a defect upstream worth surfacing loudly rather than a
    /// row this method should quietly drop or degrade.
    /// </summary>
    static DateTimeOffset ParseTimestamp(string timestamp) =>
        DateTimeOffset.Parse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
