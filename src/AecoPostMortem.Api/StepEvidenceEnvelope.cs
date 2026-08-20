using System.Text.Json.Serialization;

namespace AecoPostMortem.Api;

/// <summary>
/// FR-21 part 2 of 3 (S-52, issue #16): the inspector's Thinking tab — the readable reasoning
/// recorded for a step, "where any exists" (the story's own wording). Closed to exactly two shapes
/// behind a private constructor, the same trick <see cref="SessionTokenFiguresEnvelope"/> and
/// <see cref="SuggestionEnvelope"/> already use: "no reasoning" is a stated, explicit value, never a
/// blank panel a client could render nothing for.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(Present), "present")]
[JsonDerivedType(typeof(Unavailable), "unavailable")]
public abstract record ThinkingEnvelope
{
    private ThinkingEnvelope()
    {
    }

    public sealed record Present : ThinkingEnvelope
    {
        public required string Text { get; init; }
    }

    /// <summary>Covers three distinct reasons a prompt step carries no readable reasoning: the model
    /// wrote only provider-encrypted <c>reasoningOpaque</c>, no <c>assistant.message</c> event was
    /// found within the turn at all, or the step is not a prompt in the first place (Thinking is
    /// "recorded per assistant message" — the mockup's own wording). <see cref="Reason"/> states
    /// which, so this reads as a designed state rather than an unexplained absence.</summary>
    public sealed record Unavailable : ThinkingEnvelope
    {
        public required string Reason { get; init; }
    }
}

/// <summary>
/// FR-21 part 2 of 3 (S-52, issue #16): the inspector's Raw tab — "the provenance guarantee made
/// clickable, not a debugging affordance" (the story's own edge case). Closed to exactly two shapes:
/// <see cref="Present"/> carries the literal event payload that produced the step, and
/// <see cref="Skipped"/> is the explicit state for a step whose raw event cannot be found — the edge
/// case's own words, "shows that fact rather than an empty panel."
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(Present), "present")]
[JsonDerivedType(typeof(Skipped), "skipped")]
public abstract record RawStepEventEnvelope
{
    private RawStepEventEnvelope()
    {
    }

    public sealed record Present : RawStepEventEnvelope
    {
        public required string EventType { get; init; }

        /// <summary>The event's own payload text, verbatim — <see cref="Data.RawEvent.Payload"/> is
        /// already byte-exact (FR-2), so this is a pass-through, never a re-serialisation that could
        /// drift from what ingest actually stored.</summary>
        public required string Payload { get; init; }
    }

    public sealed record Skipped : RawStepEventEnvelope
    {
        public required string Reason { get; init; }
    }
}

/// <summary>The Detail tab needs no separate contract — every field it renders is already on the
/// tape's own <see cref="SessionTapeStepEnvelope"/>, which a client has in hand the moment a step is
/// selected. This type is only the Thinking/Raw half, fetched per selected step.</summary>
public sealed record StepEvidenceEnvelope
{
    public required ThinkingEnvelope Thinking { get; init; }

    public required RawStepEventEnvelope Raw { get; init; }
}
