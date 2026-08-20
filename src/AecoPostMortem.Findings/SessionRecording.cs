using System.Globalization;
using AecoPostMortem.Data.Execution;

namespace AecoPostMortem.Findings;

/// <summary>
/// FR-21, part 1 of 3 (S-08, issue #15): one step on the Flight Recorder's tape. The five kinds
/// name every step shape the story's own Gherkin names — "hooks, prompts, skills, tool calls and
/// MCP calls" — with <see cref="Prompt"/> standing for one assistant turn (Copilot's event log
/// carries no separate "prompt" entity; a turn is the closest bounded shape a prompt/response cycle
/// has) and <see cref="McpCall"/> a <see cref="ToolCall"/> that names an MCP server, kept distinct
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
/// One tape entry. <see cref="StepId"/> is the underlying entity's own natural key within its
/// session (a <see cref="Turn.TurnId"/>, <see cref="ToolCall.ToolCallId"/> or event-scoped
/// <c>EventId</c>) — stable across a re-render and the natural target a later story's finding chip
/// (S-52/S-53) would attach to. <see cref="Label"/> has no message text on it: <c>Turn</c> carries
/// none (<c>AecoPostMortem.Data/CLAUDE.md</c> — "messages are read from RAW"), so a prompt step is
/// labelled by its outcome instead of a transcript excerpt this layer cannot see.
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

    public TimeSpan? Elapsed { get; init; }

    public required int TurnCount { get; init; }

    public required int ToolCallCount { get; init; }

    public required int SubagentCount { get; init; }

    public required int SkillCount { get; init; }

    public int? ModelCount { get; init; }

    public required SessionTokenFigures ContextSize { get; init; }
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

    /// <summary>
    /// Builds a session's masthead and tape from plain, already-resolved <c>Data.Execution</c>
    /// rows — the same reasoning <c>HookFailureFinding.Build</c> and <c>AbortedTurnFinding.Build</c>
    /// give for taking plain inputs rather than reading through <c>PostMortemContext</c> directly:
    /// nothing in this project may query the store on its own, and the caller (here,
    /// <c>AecoPostMortem.Api</c>) is the one that knows where its rows came from.
    /// </summary>
    public static SessionRecording Build(
        Session session,
        IReadOnlyList<Turn> turns,
        IReadOnlyList<ToolCall> toolCalls,
        IReadOnlyList<Agent> agents,
        IReadOnlyList<Skill> skills,
        IReadOnlyList<Hook> hooks)
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
            steps.Add(BuildStep(
                SessionTapeStepKind.Prompt, turn.TurnId, turn.Outcome.ToString(), turn.StartedAt,
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
        };
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
