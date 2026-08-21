using System.Text.Json;
using AecoPostMortem.Data;
using AecoPostMortem.Ingestion;

namespace AecoPostMortem.Api;

/// <summary>
/// A <see cref="Findings.SessionTapeStepKind.Prompt"/> step's own real text — the same narrow-RAW-read
/// pattern <see cref="HookFailureEventLookup"/>/<see cref="DeclaredIntentLookup"/>/
/// <see cref="StepEvidenceLookup"/> already use. <see cref="Findings.SessionTapeStep.Label"/> is
/// deliberately left unchanged (still the turn's own <c>Outcome</c>) — <c>Findings</c> has no RAW
/// access and gains none here; this is a second, additive fact resolved at this layer instead, the
/// same split <see cref="StepEvidenceLookup.FindThinkingForPromptSteps"/> already draws for a prompt
/// step's readable reasoning.
///
/// A <c>user.message</c> event's own <c>data.content</c> carries the literal prompt text, verified
/// against the live reference corpus — unlike <c>data.transformedContent</c>, which wraps it in
/// system-injected <c>&lt;current_datetime&gt;</c>/<c>&lt;system_reminder&gt;</c> text, <c>content</c>
/// is exactly what the operator typed. It is joined to a <c>Prompt</c> step by <c>interactionId</c>:
/// <c>user.message.data.interactionId</c> matches the same session's <c>assistant.turn_start.data
/// .interactionId</c> — the event a <c>Prompt</c> step's own <c>StepId</c> (<c>Turn.TurnId</c>) is
/// already resolved from by <see cref="StepEvidenceLookup.Find"/>'s <c>"turnId"</c> match, reused here
/// rather than a second identity scheme.
/// </summary>
public static class PromptTextLookup
{
    /// <summary>
    /// One pass over <paramref name="sessionEvents"/>, resolving every requested step id at once —
    /// the same batch shape <see cref="StepEvidenceLookup.FindThinkingForPromptSteps"/> uses, for the
    /// same reason: bounded by the caller's own prompt step ids, never the whole tape's step count.
    /// A step id with no matching <c>turn_start</c>, or a <c>turn_start</c> whose own <c>interactionId</c>
    /// resolves no <c>user.message</c>, is simply absent from the result — "absence in, absence out",
    /// the same discipline <see cref="HookFailureEventLookup.Find"/> already follows rather than
    /// carrying a placeholder entry with no text to show.
    /// </summary>
    public static IReadOnlyDictionary<string, string> FindForPromptSteps(
        IReadOnlyList<RawEvent> sessionEvents,
        IReadOnlyCollection<string> promptStepIds)
    {
        ArgumentNullException.ThrowIfNull(sessionEvents);
        ArgumentNullException.ThrowIfNull(promptStepIds);

        var ordered = sessionEvents.OrderBy(e => e.Sequence).ToList();

        var interactionIdByTurnId = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in ordered.Where(e => e.EventType == "assistant.turn_start"))
        {
            if (!EventEnvelopeReader.TryRead(raw, out var envelope))
            {
                continue;
            }

            var turnId = GetString(envelope.Data, "turnId");
            var interactionId = GetString(envelope.Data, "interactionId");

            // A repeated turnId within one session (a real, documented corpus finding —
            // AecoPostMortem.Data/CLAUDE.md's "Turn is keyed by its own event id" entry) keeps its
            // first occurrence's interactionId, matching StepEvidenceLookup.FindByDataField's own
            // first-match behaviour for the identical field — this lookup does not attempt to fix
            // that pre-existing identity ambiguity, only to stay consistent with it. Measured against
            // the live 35-session reference corpus: 20 of 25 sessions in the dominant repository have
            // at least one repeated turnId, and the worst case collapses 310 real prompt steps onto
            // only 73 distinct StepIds — several unrelated turns therefore share one resolved
            // PromptText. This is Findings.SessionTapeStep's own pre-existing StepId choice
            // (Turn.TurnId, a display counter, rather than Turn.EventId — the identity Data.Execution
            // .Turn itself was re-keyed to for the identical reason), not something introduced here;
            // fixing it means widening SessionTapeStep.StepId itself, a larger change than this
            // lookup's own scope (it would also touch StepEvidenceLookup and every StepId consumer,
            // including the wire route and the frontend's own DOM ids) — left as a follow-up.
            if (turnId is not null && interactionId is not null)
            {
                interactionIdByTurnId.TryAdd(turnId, interactionId);
            }
        }

        var contentByInteractionId = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in ordered.Where(e => e.EventType == "user.message"))
        {
            if (!EventEnvelopeReader.TryRead(raw, out var envelope))
            {
                continue;
            }

            var interactionId = GetString(envelope.Data, "interactionId");
            var content = GetString(envelope.Data, "content");

            if (interactionId is not null && content is { Length: > 0 })
            {
                contentByInteractionId.TryAdd(interactionId, content);
            }
        }

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var stepId in promptStepIds)
        {
            if (interactionIdByTurnId.TryGetValue(stepId, out var interactionId)
                && contentByInteractionId.TryGetValue(interactionId, out var content))
            {
                result[stepId] = content;
            }
        }

        return result;
    }

    static string? GetString(JsonElement data, string property) =>
        data.ValueKind == JsonValueKind.Object
        && data.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
