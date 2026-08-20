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
/// (a turn's <c>turnId</c>, a tool call's <c>toolCallId</c>, or a skill/hook's own envelope
/// <c>id</c>, per <c>SessionRecording.cs</c>'s own remarks), so this is a lookup by that same field,
/// not a new identity scheme.
/// </summary>
public static class StepEvidenceLookup
{
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
            SessionTapeStepKind.Prompt => FindByDataField(ordered, "assistant.turn_start", "turnId", stepId),
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
                Thinking = new ThinkingEnvelope.Unavailable
                {
                    Reason = "No raw event was found for this step; it may have been skipped at ingest.",
                },
                Raw = new RawStepEventEnvelope.Skipped
                {
                    Reason = "No raw event was found for this step; it may have been skipped at ingest.",
                },
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
        };
    }

    /// <summary>A prompt step is a whole <c>Turn</c>, bounded by its own <c>turn_start</c> and the
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
            }
        }

        if (readable.Count > 0)
        {
            return new ThinkingEnvelope.Present { Text = string.Join("\n\n", readable) };
        }

        return sawOpaque
            ? new ThinkingEnvelope.Unavailable
            {
                Reason = "The model's reasoning for this step is provider-encrypted and cannot be read.",
            }
            : new ThinkingEnvelope.Unavailable
            {
                Reason = "No reasoning was recorded for this step.",
            };
    }

    static (RawEvent Raw, EventEnvelope Envelope)? FindByDataField(
        List<RawEvent> ordered, string eventType, string dataField, string expectedValue)
    {
        foreach (var raw in ordered.Where(e => e.EventType == eventType))
        {
            if (EventEnvelopeReader.TryRead(raw, out var envelope)
                && GetString(envelope.Data, dataField) == expectedValue)
            {
                return (raw, envelope);
            }
        }

        return null;
    }

    static (RawEvent Raw, EventEnvelope Envelope)? FindByEnvelopeId(
        List<RawEvent> ordered, string eventType, string expectedId)
    {
        foreach (var raw in ordered.Where(e => e.EventType == eventType))
        {
            if (EventEnvelopeReader.TryRead(raw, out var envelope) && envelope.Id == expectedId)
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
