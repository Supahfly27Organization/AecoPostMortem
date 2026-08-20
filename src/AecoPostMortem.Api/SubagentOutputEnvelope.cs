using System.Text.Json.Serialization;

namespace AecoPostMortem.Api;

/// <summary>
/// FR-22 (S-09, issue #18): a subagent lane's own report, "the report it actually produced" — the
/// story's own wording — never the parent's truncated <c>read_agent</c> stub. Closed to exactly
/// three shapes behind a private constructor, the same trick <see cref="ThinkingEnvelope"/> and
/// <see cref="SessionTokenFiguresEnvelope"/> already use: which of "a real report", "nothing
/// recorded" or "the subagent failed" applies is a stated, explicit value, never a blank lane a
/// client could render nothing for or infer from an absent field.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(Present), "present")]
[JsonDerivedType(typeof(NotRecorded), "notRecorded")]
[JsonDerivedType(typeof(Failed), "failed")]
public abstract record SubagentOutputEnvelope
{
    private SubagentOutputEnvelope()
    {
    }

    /// <summary>The last <c>assistant.message</c> carrying this subagent's own <c>agentId</c> —
    /// Scenario 1's "the report shown is the last assistant message bearing that agent's id". Never
    /// the parent's <c>read_agent</c> completion: <see cref="SubagentOutputLookup.Find"/> never reads
    /// a <c>tool.execution_complete</c> result at all, so that stub cannot reach this shape by
    /// construction.</summary>
    public sealed record Present : SubagentOutputEnvelope
    {
        public required string Text { get; init; }
    }

    /// <summary>Scenario 3: a subagent that produced no messages under its own id — stated
    /// explicitly, never a silent fall-back to the parent's stub.</summary>
    public sealed record NotRecorded : SubagentOutputEnvelope
    {
        public required string Reason { get; init; }
    }

    /// <summary>Scenario 4: a subagent whose outcome is <c>subagent.failed</c> — the failure and its
    /// recorded error (<c>Data.Execution.Agent.Error</c>) take priority over any output lookup, the
    /// same "the more urgent, more specific claim wins" ordering
    /// <c>SessionRecording.DetermineStatus</c> already gives its own two checks.</summary>
    public sealed record Failed : SubagentOutputEnvelope
    {
        public required string Error { get; init; }
    }
}
