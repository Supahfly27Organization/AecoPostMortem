using System.Text.Json;
using AecoPostMortem.Data;
using AecoPostMortem.Data.Execution;
using AecoPostMortem.Findings;

namespace AecoPostMortem.Ingestion;

/// <summary>
/// FR-8/FR-9: one session's RAW events, rebuilt into turns, tool calls and agents with correct
/// ownership. Pure and deterministic — the only inputs are the <see cref="RawEvent"/>s themselves,
/// read in <see cref="RawEvent.Sequence"/> order, so two builds against the same events always agree
/// (the same discipline <c>DerivedSchema</c> asks of anything under <c>Execution/</c>).
/// </summary>
public static class ExecutionRecordBuilder
{
    /// <summary>The <c>task</c> tool that spawns a subagent (FR-9). Naming it here, not in
    /// <c>AecoPostMortem.Rules</c>, keeps Repo Rule 6's invariant intact — this project is allowed to
    /// name a tool, the checker project never sees one.</summary>
    const string SpawningToolName = "task";

    public static ExecutionRecord Build(string sessionId, IEnumerable<RawEvent> events)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(events);

        var parsed = new List<(RawEvent Raw, EventEnvelope Envelope)>();
        foreach (var raw in events.Where(e => e.SessionId == sessionId).OrderBy(e => e.Sequence))
        {
            if (EventEnvelopeReader.TryRead(raw, out var envelope))
            {
                parsed.Add((raw, envelope));
            }
        }

        var causality = BuildCausality(parsed);
        var (turns, turnAtSequence) = WalkTurns(sessionId, parsed);
        var toolCalls = BuildToolCalls(sessionId, parsed, turnAtSequence);
        var (agents, spawnCheck) = BuildAgents(sessionId, parsed);

        return new ExecutionRecord(turns, toolCalls, agents, spawnCheck, causality);
    }

    /// <summary>Scenario 1: "each event's id and parentId form a chain across the whole session."
    /// The last envelope wins for a duplicate id, the same tolerance RAW's own identity index
    /// applies elsewhere — this is a read of what was reconstructed, not a second uniqueness
    /// constraint.</summary>
    static IReadOnlyDictionary<string, string?> BuildCausality(List<(RawEvent Raw, EventEnvelope Envelope)> parsed)
    {
        var causality = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (_, envelope) in parsed)
        {
            causality[envelope.Id] = envelope.ParentId;
        }

        return causality;
    }

    /// <summary>
    /// Turn boundaries come from <c>assistant.turn_start</c>/<c>turn_end</c> (Scenario 1); an
    /// unmatched <c>abort</c> closes the currently open turn as <see cref="TurnOutcome.Aborted"/> —
    /// the measured 9-event gap between 2,384 turn starts and 2,375 ends equals the measured 9
    /// <c>abort</c> events, so this is not a guess. A turn still open when the events run out is
    /// <see cref="TurnOutcome.Unfinished"/>. Also returns, per event <see cref="RawEvent.Sequence"/>,
    /// which turn (if any) was open at that point — <see cref="BuildToolCalls"/> uses it to assign
    /// <see cref="ToolCall.TurnId"/> without re-deriving the state machine.
    /// </summary>
    static (IReadOnlyList<Turn> Turns, IReadOnlyDictionary<long, string?> TurnAtSequence) WalkTurns(
        string sessionId,
        List<(RawEvent Raw, EventEnvelope Envelope)> parsed)
    {
        var turns = new List<Turn>();
        var turnAtSequence = new Dictionary<long, string?>();

        string? openEventId = null;
        string? openTurnId = null;
        string? openStartedAt = null;
        long outputTokens = 0;
        var hasOutputTokens = false;

        foreach (var (raw, envelope) in parsed)
        {
            turnAtSequence[raw.Sequence] = openTurnId;

            switch (raw.EventType)
            {
                case "assistant.turn_start":
                    if (openEventId is not null)
                    {
                        // A turn was still open when the next one started; close it as unfinished
                        // rather than let it silently vanish.
                        turns.Add(CloseTurn(sessionId, openEventId, openTurnId!, openStartedAt!, null, TurnOutcome.Unfinished, null, ReadTokens()));
                    }

                    openEventId = envelope.Id;
                    openTurnId = GetString(envelope.Data, "turnId");
                    openStartedAt = raw.Timestamp;
                    outputTokens = 0;
                    hasOutputTokens = false;
                    break;

                case "assistant.turn_end":
                    var endTurnId = GetString(envelope.Data, "turnId");
                    if (openEventId is not null && (endTurnId is null || endTurnId == openTurnId))
                    {
                        turns.Add(CloseTurn(sessionId, openEventId, openTurnId!, openStartedAt!, raw.Timestamp, TurnOutcome.Completed, null, ReadTokens()));
                        openEventId = null;
                        openTurnId = null;
                        openStartedAt = null;
                    }

                    // A turn_end whose turnId names a turn other than the one currently open is
                    // not observed anywhere in the reference corpus (2,384 starts vs. 2,375 ends,
                    // with all 9 missing explained one-for-one by `abort`). Rather than guess at
                    // what it would mean, this event is a no-op: the open turn stays open, to be
                    // closed by its own end, an abort, or end-of-session (Unfinished).
                    break;

                case "abort":
                    if (openEventId is not null)
                    {
                        var reason = GetString(envelope.Data, "reason");
                        turns.Add(CloseTurn(sessionId, openEventId, openTurnId!, openStartedAt!, raw.Timestamp, TurnOutcome.Aborted, reason, ReadTokens()));
                        openEventId = null;
                        openTurnId = null;
                        openStartedAt = null;
                    }

                    break;

                case "assistant.message":
                    if (openEventId is not null && envelope.AgentId is null && GetLong(envelope.Data, "outputTokens") is { } tokens)
                    {
                        outputTokens += tokens;
                        hasOutputTokens = true;
                    }

                    break;
            }
        }

        if (openEventId is not null)
        {
            turns.Add(CloseTurn(sessionId, openEventId, openTurnId!, openStartedAt!, null, TurnOutcome.Unfinished, null, ReadTokens()));
        }

        return (turns, turnAtSequence);

        long? ReadTokens() => hasOutputTokens ? outputTokens : null;
    }

    static Turn CloseTurn(
        string sessionId,
        string eventId,
        string turnId,
        string startedAt,
        string? endedAt,
        TurnOutcome outcome,
        string? abortReason,
        long? outputTokens) =>
        new()
        {
            SessionId = sessionId,
            EventId = eventId,
            TurnId = turnId,
            StartedAt = startedAt,
            EndedAt = endedAt,
            Outcome = outcome,
            AbortReason = abortReason,
            OutputTokens = outputTokens,
            OwnerKind = OwnerKind.Main,
            AgentId = null,
        };

    /// <summary>Tool calls from the <c>tool.execution_start</c>/<c>execution_complete</c> pair
    /// (Scenario 1). Ownership (Scenario 2) reads the envelope <c>agentId</c> directly — absence
    /// means main thread. <see cref="ToolCall.TurnId"/> is only ever set for a main-thread call: the
    /// data map measured zero <c>agentId</c> on every <c>turn_start</c>/<c>turn_end</c>, so a
    /// subagent's own tool calls have no turn to belong to.</summary>
    static IReadOnlyList<ToolCall> BuildToolCalls(
        string sessionId,
        List<(RawEvent Raw, EventEnvelope Envelope)> parsed,
        IReadOnlyDictionary<long, string?> turnAtSequence)
    {
        var starts = new Dictionary<string, (RawEvent Raw, EventEnvelope Envelope)>(StringComparer.Ordinal);
        var completions = new Dictionary<string, (RawEvent Raw, EventEnvelope Envelope)>(StringComparer.Ordinal);

        foreach (var (raw, envelope) in parsed)
        {
            if (raw.EventType == "tool.execution_start" && GetString(envelope.Data, "toolCallId") is { } startId)
            {
                starts[startId] = (raw, envelope);
            }
            else if (raw.EventType == "tool.execution_complete" && GetString(envelope.Data, "toolCallId") is { } completeId)
            {
                completions[completeId] = (raw, envelope);
            }
        }

        var toolCalls = new List<ToolCall>();
        foreach (var (toolCallId, start) in starts)
        {
            var hasCompletion = completions.TryGetValue(toolCallId, out var completion);
            var ownerKind = start.Envelope.AgentId is null ? OwnerKind.Main : OwnerKind.Agent;

            toolCalls.Add(new ToolCall
            {
                SessionId = sessionId,
                ToolCallId = toolCallId,
                ToolName = GetString(start.Envelope.Data, "toolName") ?? string.Empty,
                StartedAt = start.Raw.Timestamp,
                CompletedAt = hasCompletion ? completion.Raw.Timestamp : null,
                Success = hasCompletion ? GetBool(completion.Envelope.Data, "success") : null,
                Path = GetArgumentPath(start.Envelope.Data),
                ResultSizeBytes = hasCompletion ? GetResultSizeBytes(completion.Envelope.Data) : null,
                McpServerName = GetString(start.Envelope.Data, "mcpServerName"),
                McpToolName = GetString(start.Envelope.Data, "mcpToolName"),
                TurnId = ownerKind == OwnerKind.Main ? turnAtSequence.GetValueOrDefault(start.Raw.Sequence) : null,
                OwnerKind = ownerKind,
                AgentId = start.Envelope.AgentId,
            });
        }

        return toolCalls.OrderBy(tc => tc.StartedAt, StringComparer.Ordinal).ThenBy(tc => tc.ToolCallId, StringComparer.Ordinal).ToArray();
    }

    /// <summary>FR-9: <c>subagent.started</c> resolved against the spawning <c>task</c> call
    /// (Scenario 3), nesting derived from that call's own <c>agentId</c> (Scenario 5), and outcome
    /// folded in from <c>subagent.completed</c>/<c>subagent.failed</c> — matched by envelope
    /// <c>agentId</c>, which the data map measured on 100% of both. A completion that carries none of
    /// its four cost fields is <see cref="AgentOutcome.CompletedCostUnknown"/> rather than
    /// <see cref="AgentOutcome.Completed"/> with zeroes, satisfying <c>ck_agent_cost</c> (only
    /// <c>Completed</c> may carry non-null cost columns) without inventing a price for the measured
    /// 247 of 462 completions that report none.</summary>
    static (IReadOnlyList<Agent> Agents, CheckRegistryEntry SpawnCheck) BuildAgents(
        string sessionId,
        List<(RawEvent Raw, EventEnvelope Envelope)> parsed)
    {
        var taskSpawnerAgentId = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (raw, envelope) in parsed)
        {
            if (raw.EventType == "tool.execution_start"
                && GetString(envelope.Data, "toolName") == SpawningToolName
                && GetString(envelope.Data, "toolCallId") is { } taskToolCallId)
            {
                taskSpawnerAgentId[taskToolCallId] = envelope.AgentId;
            }
        }

        var completedByAgent = new Dictionary<string, (RawEvent Raw, EventEnvelope Envelope)>(StringComparer.Ordinal);
        var failedByAgent = new Dictionary<string, (RawEvent Raw, EventEnvelope Envelope)>(StringComparer.Ordinal);
        foreach (var (raw, envelope) in parsed)
        {
            if (envelope.AgentId is not { } agentId)
            {
                continue;
            }

            if (raw.EventType == "subagent.completed")
            {
                completedByAgent[agentId] = (raw, envelope);
            }
            else if (raw.EventType == "subagent.failed")
            {
                failedByAgent[agentId] = (raw, envelope);
            }
        }

        var agents = new List<Agent>();
        var examined = 0;
        var unresolved = 0;

        foreach (var (raw, envelope) in parsed)
        {
            if (raw.EventType != "subagent.started" || GetString(envelope.Data, "toolCallId") is not { } agentId)
            {
                continue;
            }

            examined++;

            // Scenario 3: resolved against the task call that produced it, or reported, not dropped.
            if (!taskSpawnerAgentId.TryGetValue(agentId, out var parentAgentId))
            {
                unresolved++;
                continue;
            }

            agents.Add(BuildAgent(sessionId, raw, envelope, agentId, parentAgentId, completedByAgent, failedByAgent));
        }

        return (agents, SpawnResolutionCheck.From(examined, unresolved));
    }

    static Agent BuildAgent(
        string sessionId,
        RawEvent startedRaw,
        EventEnvelope startedEnvelope,
        string agentId,
        string? parentAgentId,
        Dictionary<string, (RawEvent Raw, EventEnvelope Envelope)> completedByAgent,
        Dictionary<string, (RawEvent Raw, EventEnvelope Envelope)> failedByAgent)
    {
        var outcome = AgentOutcome.Running;
        long? totalTokens = null;
        int? totalToolCalls = null;
        long? durationMs = null;
        string? model = null;
        string? error = null;

        if (failedByAgent.TryGetValue(agentId, out var failed))
        {
            outcome = AgentOutcome.Failed;
            error = GetString(failed.Envelope.Data, "error");
        }
        else if (completedByAgent.TryGetValue(agentId, out var completed))
        {
            var tokens = GetLong(completed.Envelope.Data, "totalTokens");
            var toolCalls = GetInt(completed.Envelope.Data, "totalToolCalls");
            var duration = GetLong(completed.Envelope.Data, "durationMs");
            var completedModel = GetString(completed.Envelope.Data, "model");

            if (tokens is null && toolCalls is null && duration is null && completedModel is null)
            {
                outcome = AgentOutcome.CompletedCostUnknown;
            }
            else
            {
                outcome = AgentOutcome.Completed;
                totalTokens = tokens;
                totalToolCalls = toolCalls;
                durationMs = duration;
                model = completedModel;
            }
        }

        return new Agent
        {
            SessionId = sessionId,
            AgentId = agentId,
            SpawningToolCallId = agentId,
            ParentAgentId = parentAgentId,
            Name = GetString(startedEnvelope.Data, "agentName") ?? string.Empty,
            DisplayName = GetString(startedEnvelope.Data, "agentDisplayName") ?? string.Empty,
            Description = GetString(startedEnvelope.Data, "agentDescription"),
            StartedAt = startedRaw.Timestamp,
            Outcome = outcome,
            TotalTokens = totalTokens,
            TotalToolCalls = totalToolCalls,
            DurationMs = durationMs,
            Model = model,
            Error = error,
        };
    }

    static string? GetArgumentPath(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty("arguments", out var argumentsProperty))
        {
            return null;
        }

        var arguments = ToolArguments.Parse(argumentsProperty.GetRawText());
        if (arguments.Kind != ToolArgumentKind.Object)
        {
            return null;
        }

        return arguments.TryGetProperty("path", out var pathValue) && pathValue.ValueKind == JsonValueKind.String
            ? pathValue.GetString()
            : null;
    }

    /// <summary>Derived from content length, not a field Copilot writes directly — the data map's
    /// "Derived, from <c>result.content</c> length" mapping for <c>ResultSizeBytes</c>. Does not
    /// fall back to <c>toolTelemetry.metrics.resultLength</c> (the data map's documented secondary
    /// source, covering 39% where <c>result.content</c> covers 98%) — a deliberate scope cut, not an
    /// oversight; see this project's CLAUDE.md.</summary>
    static long? GetResultSizeBytes(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object
            || !data.TryGetProperty("result", out var result)
            || result.ValueKind != JsonValueKind.Object
            || !result.TryGetProperty("content", out var content)
            || content.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return content.GetString() is { } text ? System.Text.Encoding.UTF8.GetByteCount(text) : null;
    }

    static string? GetString(JsonElement data, string property) =>
        data.ValueKind == JsonValueKind.Object
        && data.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    static long? GetLong(JsonElement data, string property) =>
        data.ValueKind == JsonValueKind.Object
        && data.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt64(out var result)
            ? result
            : null;

    // Never throws on an out-of-range value - every other helper in this file degrades an
    // unexpected shape to null rather than raising, and a tool-call count is no exception even
    // though today's corpus never approaches int.MaxValue.
    static int? GetInt(JsonElement data, string property) =>
        GetLong(data, property) is { } value && value >= int.MinValue && value <= int.MaxValue ? (int)value : null;

    static bool? GetBool(JsonElement data, string property) =>
        data.ValueKind == JsonValueKind.Object && data.TryGetProperty(property, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null,
            }
            : null;
}

/// <summary>
/// One session's reconstructed execution record: turns, tool calls and agents (FR-8, FR-9), the
/// causality map Scenario 1 asks for, and the spawn-resolution check that always registers itself
/// (Scenario 4) whether or not anything failed to resolve.
/// </summary>
public sealed record ExecutionRecord(
    IReadOnlyList<Turn> Turns,
    IReadOnlyList<ToolCall> ToolCalls,
    IReadOnlyList<Agent> Agents,
    CheckRegistryEntry SpawnResolutionCheck,
    IReadOnlyDictionary<string, string?> Causality);
