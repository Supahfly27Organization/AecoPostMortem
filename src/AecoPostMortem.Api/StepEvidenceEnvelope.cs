using System.Text.Json.Serialization;
using AecoPostMortem.Ingestion;

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

        /// <summary>FR-23 (S-10, issue #19): populated only for the provider-encryption reason —
        /// the session's own measured readable share of reasoning, one entry per model this session
        /// actually used for a reasoning-bearing main-thread message, never a corpus-wide constant
        /// and never averaged across models (the story's own edge case: two models get two
        /// figures). Null for the other two <see cref="Reason"/> cases this type carries (no raw
        /// event found; a step kind that carries no reasoning of its own), where there is no
        /// per-model encryption question to answer.</summary>
        public IReadOnlyList<ModelReasoningReadability>? ReadabilityByModel { get; init; }
    }
}

/// <summary>
/// FR-23 (S-10, issue #19): one model's measured readable-reasoning share, computed over this
/// session's own main-thread <c>assistant.message</c> events that carried any reasoning at all
/// (readable <c>reasoningText</c> or provider-encrypted <c>reasoningOpaque</c>) — session-scoped,
/// never a corpus-wide figure (the PRD's own worked example: measured 3.5% readable on
/// <c>gpt-5.4</c> against measured 88.2% on <c>claude-sonnet-4.5</c>, in the same corpus).
/// <see cref="ReadableCount"/> and <see cref="TotalCount"/> are both <c>required</c>; the share has
/// no setter — the same "a rate never appears without its counts" reasoning
/// <see cref="AecoPostMortem.Rules.FailureRate"/> already documents for its own percentage.
/// </summary>
public sealed record ModelReasoningReadability
{
    public required string Model { get; init; }

    public required int ReadableCount { get; init; }

    public required int TotalCount { get; init; }

    public double ReadableSharePercent => TotalCount == 0 ? 0d : 100d * ReadableCount / TotalCount;
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

    /// <summary>A tool call's own result — the literal <c>tool.execution_complete</c> payload,
    /// verbatim, the same pass-through discipline <see cref="Raw"/> already gives the call's own
    /// <c>tool.execution_start</c>. Reuses <see cref="RawStepEventEnvelope"/> rather than a new type:
    /// this is not a re-parsed <c>content</c>/<c>detailedContent</c> shape (confirmed against the
    /// live 35-session reference corpus to always be object-shaped on success — never the bare-string
    /// shape this project's own <c>ToolArguments.cs</c> precedent exists for), it is the same
    /// "the literal event payload, or a stated absence" question <see cref="Raw"/> already answers,
    /// asked of a second event. <see cref="RawStepEventEnvelope.Skipped"/> covers three distinct
    /// reasons, each named so a caller can tell them apart: this step kind never produces a tool
    /// result at all (a <c>Prompt</c>/<c>Skill</c>/<c>Hook</c> step); a <c>ToolCall</c>/<c>McpCall</c>
    /// step whose own <c>tool.execution_complete</c> was never recorded — still running, or the
    /// session ended mid-call — never an empty string read as "the result was empty"; or no raw event
    /// was found for the step at all (the same reason <see cref="Raw"/> reports for that case).</summary>
    public required RawStepEventEnvelope Result { get; init; }

    /// <summary>What triggered a hook — <c>hook.start.data.input</c>'s own <c>toolName</c>/
    /// <c>toolArgs</c>/<c>toolResult</c>, verified against the live 35-session reference corpus. A
    /// hook row today says a hook ran but not what it ran in response to; this closes that gap.
    /// Populated only for a <c>Hook</c> step — every other step kind states plainly that it has no
    /// trigger at all, the same short-circuit <see cref="Result"/> already applies to a non-tool-call
    /// step kind. See <see cref="HookTriggerEnvelope"/>'s own doc comment for the full shape.</summary>
    public required HookTriggerEnvelope Trigger { get; init; }
}

/// <summary>
/// What triggered a hook (FR-17's sibling gap — a hook row said a hook ran, never what it ran in
/// response to). A closed two-shape union behind a private constructor, the same
/// <see cref="RawStepEventEnvelope"/>/<see cref="ThinkingEnvelope"/> mechanism: a <c>postToolUse</c>
/// hook's real trigger is <see cref="ToolInvocation"/>, and every other case — a
/// <c>sessionStart</c> hook (structurally no tool trigger: it fires at session start, carrying
/// <c>initialPrompt</c>/<c>source</c>/<c>cwd</c> instead of <c>toolName</c>, verified against 35 real
/// <c>sessionStart</c> events in the live reference corpus, none with a <c>toolName</c>), a non-hook
/// step kind, or a hook step whose own <c>hook.start</c> cannot be found — is <see cref="Absent"/>
/// with a reason distinguishing which. "No trigger" is therefore always a distinct, stated value,
/// never an empty string a client could render as though the trigger were blank or unknown.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ToolInvocation), "toolInvocation")]
[JsonDerivedType(typeof(Absent), "absent")]
public abstract record HookTriggerEnvelope
{
    private HookTriggerEnvelope()
    {
    }

    public sealed record ToolInvocation : HookTriggerEnvelope
    {
        public required string ToolName { get; init; }

        public required HookTriggerArguments Arguments { get; init; }

        /// <summary>The literal <c>toolResult</c> JSON text, verbatim — served whole, never
        /// truncated, the same precedent <see cref="RawStepEventEnvelope.Present.Payload"/>'s own doc
        /// comment states: measured max 199,831 characters (~200 KB) across the live 35-session
        /// reference corpus (a <c>github-mcp-server-get_file_contents</c> call), and only 22 of 2,992
        /// real <c>postToolUse</c> trigger results exceed the 43 KB max this project already served
        /// whole for a tool call's own result. The client bounds the rendered block instead
        /// (<c>web/CLAUDE.md</c>'s matching <c>max-height</c>/<c>overflow-y</c> entry). Measured 100%
        /// (2,992 of 2,992) of real <c>postToolUse</c> triggers carry one, but this is still modelled
        /// as nullable rather than assumed — a trigger genuinely carrying none states that fact as
        /// <see langword="null"/>, never an empty string standing in for "no result."</summary>
        public required string? Result { get; init; }
    }

    public sealed record Absent : HookTriggerEnvelope
    {
        public required string Reason { get; init; }
    }
}

/// <summary>A hook trigger's own <c>toolArgs</c>, parsed the identical polymorphic way
/// <see cref="ToolArguments"/> parses <c>tool.execution_start.data.arguments</c> (FR-4): object for
/// most tools, a bare JSON string for <c>apply_patch</c>'s whole patch body (a measured 840 of 2,992
/// real <c>postToolUse</c> <c>hook.start</c> events in the live reference corpus), never coerced.
/// <see cref="Kind"/> reuses <see cref="ToolArgumentKind"/> itself rather than a second, parallel
/// enum — the same "camelCase enum on the wire" convention <see cref="Data.Execution.OwnerKind"/>
/// already gets from the global <c>JsonStringEnumConverter</c>, no per-property override needed.
/// <see cref="Raw"/> is the value's own JSON text, exactly as read — never re-serialised.</summary>
public sealed record HookTriggerArguments
{
    public required ToolArgumentKind Kind { get; init; }

    public required string Raw { get; init; }
}
