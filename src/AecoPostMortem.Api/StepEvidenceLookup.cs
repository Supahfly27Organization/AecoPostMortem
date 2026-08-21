using System.Text.Json;
using AecoPostMortem.Data;
using AecoPostMortem.Findings;
using AecoPostMortem.Ingestion;

namespace AecoPostMortem.Api;

/// <summary>
/// FR-21 part 2 of 3 (S-52, issue #16): resolves a tape step's Raw and Thinking evidence straight
/// from a session's own <see cref="RawEvent"/>s — the same envelope reading
/// <c>AecoPostMortem.Ingestion.ExecutionRecordBuilder</c> already does to build the tape itself
/// (FR-8/FR-9), reused here rather than re-derived, since neither <see cref="Data.Execution.Turn"/>
/// nor <see cref="Data.Execution.ToolCall"/> carries a foreign key back to the <see cref="RawEvent"/>
/// row that produced it (`AecoPostMortem.Data/CLAUDE.md`: the payload stays authoritative, and
/// nothing lifts an envelope's own <c>id</c> out into a NORMALIZED column). A step's own identity —
/// <c>SessionTapeStep.StepId</c> — is exactly the field each event kind's envelope already carries
/// (a turn's, skill's or hook's own envelope <c>id</c>; a tool call's <c>data.toolCallId</c>, the one
/// natural id Copilot writes for the thing itself — per <c>SessionRecording.cs</c>'s own remarks), so
/// this is a lookup by that same field, not a new identity scheme.
///
/// A prompt step is matched on the <c>assistant.turn_start</c> envelope's own <c>id</c>, never on
/// <c>data.turnId</c>: that field is Copilot's own cycling display counter, repeated within one
/// session on a measured 20 of 25 real sessions, so matching it would resolve several unrelated turns
/// to whichever one carried the counter first (<c>SessionRecording.cs</c>'s <c>StepId</c> remarks
/// carry the full measurement). The envelope id is the identity <c>Data.Execution.Turn</c> itself is
/// keyed by, and is what a <c>Prompt</c> step's <c>StepId</c> now carries.
/// </summary>
public static class StepEvidenceLookup
{
    const string NoRawEventFoundReason =
        "No raw event was found for this step; it may have been skipped at ingest.";

    /// <summary>The "wrong step kind" reason — a <c>Prompt</c>/<c>Skill</c>/<c>Hook</c> step never
    /// produces a <c>tool.execution_complete</c> at all, so no lookup is even attempted. Named
    /// separately from <see cref="NoRecordedCompletionReason"/> so a test (or a future caller) can
    /// tell the two apart on their own terms, not merely on both being non-empty strings (code
    /// review).</summary>
    const string ResultNotApplicableReason =
        "Only a tool or MCP call produces a result; this step kind does not.";

    /// <summary>The "still running, or the session ended mid-call" reason — a real
    /// <c>ToolCall</c>/<c>McpCall</c> step whose own <c>tool.execution_complete</c> was never
    /// recorded, distinct from <see cref="ResultNotApplicableReason"/> above.</summary>
    const string NoRecordedCompletionReason =
        "No tool.execution_complete was recorded for this call; it may still be running, or the session ended before it completed.";

    public static StepEvidenceEnvelope Find(
        IReadOnlyList<RawEvent> sessionEvents,
        SessionTapeStepKind kind,
        string stepId)
    {
        ArgumentNullException.ThrowIfNull(sessionEvents);
        ArgumentException.ThrowIfNullOrWhiteSpace(stepId);

        var ordered = sessionEvents.OrderBy(e => e.Sequence).ToList();

        var anchor = kind switch
        {
            SessionTapeStepKind.Prompt => FindByEnvelopeId(ordered, "assistant.turn_start", stepId),
            SessionTapeStepKind.ToolCall or SessionTapeStepKind.McpCall =>
                FindByDataField(ordered, "tool.execution_start", "toolCallId", stepId),
            SessionTapeStepKind.Skill => FindByEnvelopeId(ordered, "skill.invoked", stepId),
            SessionTapeStepKind.Hook => FindByEnvelopeId(ordered, "hook.start", stepId),
            _ => null,
        };

        if (anchor is not { } found)
        {
            return new StepEvidenceEnvelope
            {
                Thinking = new ThinkingEnvelope.Unavailable { Reason = NoRawEventFoundReason },
                Raw = new RawStepEventEnvelope.Skipped { Reason = NoRawEventFoundReason },
                // A missing tool.execution_start (e.g. skipped at ingest) does not imply a missing
                // tool.execution_complete — the two are independent events, so a real, present
                // result must still surface here rather than inheriting the call's own absence
                // (code review).
                Result = FindResult(ordered, kind, stepId),
            };
        }

        return new StepEvidenceEnvelope
        {
            Thinking = kind == SessionTapeStepKind.Prompt
                ? FindThinking(ordered, found.Raw.Sequence)
                : new ThinkingEnvelope.Unavailable
                {
                    Reason = "Thinking is recorded per assistant message; this step kind carries none of its own.",
                },
            Raw = new RawStepEventEnvelope.Present
            {
                EventType = found.Raw.EventType,
                Payload = found.Raw.Payload,
            },
            Result = FindResult(ordered, kind, stepId),
        };
    }

    /// <summary>A tool call's own result — <c>tool.execution_complete</c>, joined by the identical
    /// <c>data.toolCallId</c> field <see cref="Find"/> already joins the call's own
    /// <c>tool.execution_start</c> on (confirmed against the live 35-session reference corpus: every
    /// tool call, MCP or not, carries this field on its own completion event). Only a
    /// <see cref="SessionTapeStepKind.ToolCall"/> or <see cref="SessionTapeStepKind.McpCall"/> step
    /// produces one at all — every other step kind reports a fixed "not applicable" reason without
    /// attempting a lookup, the same short-circuit <see cref="Find"/> already applies to
    /// <see cref="ThinkingEnvelope"/> for a non-<c>Prompt</c> step.</summary>
    static RawStepEventEnvelope FindResult(List<RawEvent> ordered, SessionTapeStepKind kind, string stepId)
    {
        if (kind != SessionTapeStepKind.ToolCall && kind != SessionTapeStepKind.McpCall)
        {
            return new RawStepEventEnvelope.Skipped { Reason = ResultNotApplicableReason };
        }

        var completion = FindByDataField(ordered, "tool.execution_complete", "toolCallId", stepId);
        if (completion is not { } found)
        {
            return new RawStepEventEnvelope.Skipped { Reason = NoRecordedCompletionReason };
        }

        return new RawStepEventEnvelope.Present
        {
            EventType = found.Raw.EventType,
            Payload = found.Raw.Payload,
        };
    }

    /// <summary>Mockup parity item #13 ("Prose in transcript"): resolves every prompt step's own
    /// readable-reasoning summary in one pass, reusing the identical <see cref="FindThinking"/>
    /// resolution <see cref="Find"/> already uses for one step on click. Bounded by the caller's own
    /// <paramref name="promptStepIds"/> — one per <c>Turn</c> (<c>SessionMasthead.TurnCount</c>),
    /// never the whole tape's step count — which is what keeps eager-resolving this at session-fetch
    /// time cheap even at this project's largest measured scale (84 turns, a session with 195 turns
    /// confirmed against the live reference corpus): <c>ordered</c> is sorted once and reused for
    /// every step, rather than each step re-sorting the session's own <see cref="RawEvent"/>s the way
    /// two separate <see cref="Find"/> calls would.</summary>
    public static IReadOnlyDictionary<string, ThinkingEnvelope> FindThinkingForPromptSteps(
        IReadOnlyList<RawEvent> sessionEvents,
        IReadOnlyCollection<string> promptStepIds)
    {
        ArgumentNullException.ThrowIfNull(sessionEvents);
        ArgumentNullException.ThrowIfNull(promptStepIds);

        var ordered = sessionEvents.OrderBy(e => e.Sequence).ToList();
        var result = new Dictionary<string, ThinkingEnvelope>(StringComparer.Ordinal);

        foreach (var stepId in promptStepIds)
        {
            var anchor = FindByEnvelopeId(ordered, "assistant.turn_start", stepId);
            result[stepId] = anchor is { } found
                ? FindThinking(ordered, found.Raw.Sequence)
                : new ThinkingEnvelope.Unavailable { Reason = NoRawEventFoundReason };
        }

        return result;
    }

    /// <summary>A prompt step is a whole <c>Turn</c>, bounded by <em>its own</em> <c>turn_start</c>
    /// (matched by that event's envelope <c>id</c>, the step's own <c>StepId</c>) and the
    /// next <c>turn_start</c> (or end of session) — the same open-turn window
    /// <c>ExecutionRecordBuilder.WalkTurns</c> tracks while building the tape itself. Every
    /// main-thread <c>assistant.message</c> inside that window is a candidate: its
    /// <c>reasoningText</c>, if present, is readable prose (measured 1,252 messages, data map Part
    /// 6); <c>reasoningOpaque</c> is provider-encrypted and never readable, regardless of how many
    /// messages carry it.</summary>
    static ThinkingEnvelope FindThinking(List<RawEvent> ordered, long turnStartSequence)
    {
        var boundary = ordered
            .Where(e => e.Sequence > turnStartSequence && e.EventType == "assistant.turn_start")
            .Select(e => (long?)e.Sequence)
            .FirstOrDefault();
        var boundarySequence = boundary ?? long.MaxValue;

        var readable = new List<string>();
        var sawOpaque = false;
        string? opaqueModel = null;

        foreach (var raw in ordered.Where(e =>
            e.Sequence > turnStartSequence && e.Sequence < boundarySequence && e.EventType == "assistant.message"))
        {
            if (!EventEnvelopeReader.TryRead(raw, out var envelope) || envelope.AgentId is not null)
            {
                // Main-thread only — a subagent's own reasoning belongs to its own step, not its
                // parent turn's, the same ownership split `ExecutionRecordBuilder.WalkTurns` applies
                // to output-token accumulation.
                continue;
            }

            if (GetString(envelope.Data, "reasoningText") is { Length: > 0 } text)
            {
                readable.Add(text);
            }
            else if (HasProperty(envelope.Data, "reasoningOpaque"))
            {
                sawOpaque = true;
                opaqueModel ??= GetString(envelope.Data, "model");
            }
        }

        if (readable.Count > 0)
        {
            return new ThinkingEnvelope.Present { Text = string.Join("\n\n", readable) };
        }

        if (sawOpaque)
        {
            // FR-23 (S-10, issue #19): name the model where the event carried one, and always
            // attach the session's own per-model readable share — "reports the measured readable
            // share for the models this session actually used" (the story's own wording), computed
            // across the whole session, not bounded to this one turn.
            return new ThinkingEnvelope.Unavailable
            {
                Reason = opaqueModel is null
                    ? "The model's reasoning for this step is provider-encrypted and cannot be read."
                    : $"This step's reasoning is provider-encrypted for {opaqueModel} and cannot be read.",
                ReadabilityByModel = ReasoningReadabilityByModel(ordered),
            };
        }

        return new ThinkingEnvelope.Unavailable
        {
            Reason = "No reasoning was recorded for this step.",
        };
    }

    /// <summary>FR-23 (S-10, issue #19): this session's own main-thread reasoning readability,
    /// grouped by model — scanned across every <c>assistant.message</c> in the session, not bounded
    /// to one turn, because "the models this session actually used" (the story's own wording) is a
    /// session-wide fact, not a per-step one. A message carrying neither <c>reasoningText</c> nor
    /// <c>reasoningOpaque</c> contributes nothing; a message that carries one of the two but no
    /// <c>model</c> field of its own is excluded entirely — there is no model to attribute it to,
    /// and folding it into an invented "unknown" bucket would manufacture a fourth figure no
    /// acceptance scenario asks for. Ordered by model name (ordinal) for deterministic output
    /// (PRD §3.8) — nothing else fixes an order for two models tied on first appearance.</summary>
    static IReadOnlyList<ModelReasoningReadability> ReasoningReadabilityByModel(List<RawEvent> ordered)
    {
        var counts = new Dictionary<string, (int Readable, int Total)>(StringComparer.Ordinal);

        foreach (var raw in ordered.Where(e => e.EventType == "assistant.message"))
        {
            if (!EventEnvelopeReader.TryRead(raw, out var envelope) || envelope.AgentId is not null)
            {
                continue;
            }

            var model = GetString(envelope.Data, "model");
            if (model is null)
            {
                continue;
            }

            var isReadable = GetString(envelope.Data, "reasoningText") is { Length: > 0 };
            var hasReasoning = isReadable || HasProperty(envelope.Data, "reasoningOpaque");
            if (!hasReasoning)
            {
                continue;
            }

            var current = counts.TryGetValue(model, out var existing) ? existing : (Readable: 0, Total: 0);
            counts[model] = (current.Readable + (isReadable ? 1 : 0), current.Total + 1);
        }

        return counts
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new ModelReasoningReadability
            {
                Model = pair.Key,
                ReadableCount = pair.Value.Readable,
                TotalCount = pair.Value.Total,
            })
            .ToList();
    }

    /// <summary>Returns the <em>last</em> matching event in sequence order, not the first — the same
    /// overwrite-on-duplicate semantics <c>Ingestion.ExecutionRecordBuilder.BuildToolCalls</c>
    /// already gives its own <c>toolCallId</c>-keyed <c>completions</c> dictionary, so this lookup and
    /// the Detail tab's derived <c>ToolCall.Success</c>/<c>.CompletedAt</c> columns can never disagree
    /// about which of two same-id events is authoritative (code review) — an essentially theoretical
    /// case on the live corpus (every measured <c>toolCallId</c> is unique per event type), but one
    /// event id should mean one answer regardless.</summary>
    static (RawEvent Raw, EventEnvelope Envelope)? FindByDataField(
        List<RawEvent> ordered, string eventType, string dataField, string expectedValue)
    {
        (RawEvent Raw, EventEnvelope Envelope)? match = null;

        foreach (var raw in ordered.Where(e => e.EventType == eventType))
        {
            if (EventEnvelopeReader.TryRead(raw, out var envelope)
                && GetString(envelope.Data, dataField) == expectedValue)
            {
                match = (raw, envelope);
            }
        }

        return match;
    }

    /// <summary>An empty envelope <c>id</c> never matches, the same rule <see cref="PromptTextLookup"/>
    /// applies when it keys its own dictionary on this field. <c>EventEnvelopeReader.TryRead</c>
    /// rejects a missing or non-string <c>id</c> but accepts <c>"id":""</c>, so without this guard
    /// every event carrying one would collide on a single empty step id and resolve to whichever came
    /// first — exactly the identity failure this lookup was changed to escape, one level down.</summary>
    static (RawEvent Raw, EventEnvelope Envelope)? FindByEnvelopeId(
        List<RawEvent> ordered, string eventType, string expectedId)
    {
        if (expectedId.Length == 0)
        {
            return null;
        }

        foreach (var raw in ordered.Where(e => e.EventType == eventType))
        {
            if (EventEnvelopeReader.TryRead(raw, out var envelope)
                && envelope.Id is { Length: > 0 }
                && envelope.Id == expectedId)
            {
                return (raw, envelope);
            }
        }

        return null;
    }

    static string? GetString(JsonElement data, string property) =>
        data.ValueKind == JsonValueKind.Object
        && data.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    static bool HasProperty(JsonElement data, string property) =>
        data.ValueKind == JsonValueKind.Object && data.TryGetProperty(property, out _);
}
