using System.Text.Json;
using AecoPostMortem.Data;
using AecoPostMortem.Ingestion;

namespace AecoPostMortem.Api;

/// <summary>
/// A <see cref="Findings.SessionTapeStepKind.Hook"/> step's own trigger — the tool name a
/// <c>postToolUse</c> hook fired in response to, resolved eagerly for the Detail tab so it needs no
/// per-step fetch, the same "additive, eager, no-fetch" shape <see cref="PromptTextLookup"/>
/// established for a Prompt step's own text. <c>hook.start.data.input.toolName</c> is present only
/// for <c>postToolUse</c> hooks — a <c>sessionStart</c> hook carries <c>data.input.initialPrompt</c>/
/// <c>.source</c>/<c>.cwd</c> instead, no <c>toolName</c> at all (verified against 35 real
/// <c>sessionStart</c> events in the live reference corpus) — so a <c>sessionStart</c> step, and any
/// step whose own <c>hook.start</c> cannot be found, is simply absent from the result: "absence in,
/// absence out," the same discipline <see cref="HookFailureEventLookup.Find"/>/
/// <see cref="PromptTextLookup.FindForPromptSteps"/> already follow rather than carrying a
/// placeholder entry with no name to show. The Raw tab's own richer trigger evidence (the full
/// arguments and result, potentially large) is a separate, on-demand read —
/// <see cref="StepEvidenceLookup.Find"/>'s own <c>Trigger</c> field — this lookup only ever resolves
/// the one short field the tape needs eagerly, deliberately not sharing a RAW pass with that fuller
/// per-step read: the two are asked at different times (eagerly for the whole tape, versus once a
/// step is actually selected) for different-sized answers, the same "two narrow readers, no shared
/// pass" split <c>PromptTextLookup</c> and <c>StepEvidenceLookup.FindThinkingForPromptSteps</c> also
/// keep separate for prompt text versus reasoning.
/// </summary>
public static class HookTriggerNameLookup
{
    /// <summary>
    /// One pass over <paramref name="sessionEvents"/>, resolving every requested step id at once —
    /// the same batch shape <see cref="PromptTextLookup.FindForPromptSteps"/> uses, for the same
    /// reason: bounded by the caller's own hook step ids, never the whole tape's step count.
    /// </summary>
    public static IReadOnlyDictionary<string, string> FindForHookSteps(
        IReadOnlyList<RawEvent> sessionEvents,
        IReadOnlyCollection<string> hookStepIds)
    {
        ArgumentNullException.ThrowIfNull(sessionEvents);
        ArgumentNullException.ThrowIfNull(hookStepIds);

        // Keyed by hook.start's own `data.hookInvocationId` — a Hook step's own StepId
        // (`Data.Execution.Hook.EventId`) — never the envelope's own `id`. `HookBuilder` deliberately
        // keys a Hook row by the pair's shared correlation id (its own doc comment: "unlike Skill,
        // neither event's own envelope id ties the two together"), and a real hook.start's envelope
        // id and its own hookInvocationId are two different values — confirmed against the live
        // reference corpus, and the identical fix `StepEvidenceLookup.Find`'s own Hook branch needed
        // (see that file's own doc comment).
        //
        // Two phases, deliberately not one: first find the *last* hook.start carrying each
        // invocation id (mirroring `StepEvidenceLookup.FindByDataField`'s own overwrite-on-duplicate
        // semantics, code review), then read a tool name off that one final envelope — never
        // "the last envelope that happened to carry a tool name," which would let an earlier, stale
        // duplicate's tool name survive underneath a later, real duplicate that carries none. This
        // keeps this eager field and `StepEvidenceLookup.FindTrigger`'s own on-demand read resolving
        // from the identical envelope for a shared invocation id — an essentially theoretical case on
        // the live corpus (every measured hookInvocationId is unique), but one event id should mean
        // one answer regardless.
        var lastEnvelopeByInvocationId = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

        foreach (var raw in sessionEvents.Where(e => e.EventType == "hook.start"))
        {
            if (!EventEnvelopeReader.TryRead(raw, out var envelope))
            {
                continue;
            }

            var invocationId = GetString(envelope.Data, "hookInvocationId");
            if (invocationId is { Length: > 0 })
            {
                lastEnvelopeByInvocationId[invocationId] = envelope.Data;
            }
        }

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var stepId in hookStepIds)
        {
            if (lastEnvelopeByInvocationId.TryGetValue(stepId, out var data)
                && GetToolName(data) is { Length: > 0 } toolName)
            {
                result[stepId] = toolName;
            }
        }

        return result;
    }

    static string? GetToolName(JsonElement data) =>
        data.ValueKind == JsonValueKind.Object
        && data.TryGetProperty("input", out var input)
        && input.ValueKind == JsonValueKind.Object
        && input.TryGetProperty("toolName", out var toolName)
        && toolName.ValueKind == JsonValueKind.String
            ? toolName.GetString()
            : null;

    static string? GetString(JsonElement data, string property) =>
        data.ValueKind == JsonValueKind.Object
        && data.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
