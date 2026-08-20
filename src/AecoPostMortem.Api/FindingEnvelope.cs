using System.Text.Json.Serialization;
using AecoPostMortem.Findings;

namespace AecoPostMortem.Api;

/// <summary>
/// FR-59: the API response contract for one served finding. Provenance is <c>required</c> on the
/// base type, so every shape carries it (Scenario 1). Three closed shapes exist beneath it — the
/// private constructor restricts derivation to the nested <see cref="General"/>,
/// <see cref="Adherence"/> and <see cref="BaseRate"/> records, the only types with access to it:
/// <list type="bullet">
/// <item><see cref="General"/> — every finding class FR-33 does not apply to. It has no
/// <c>Figure</c> member at all; there is no field on this shape a bare adherence figure could leave
/// null.</item>
/// <item><see cref="Adherence"/> — the only shape that can carry an adherence figure, through one
/// <c>required</c> member, <see cref="Adherence.Figure"/>. Assembling one without it is a compile
/// error (CS9035), the same guarantee <c>Finding.Provenance</c> already gives (issue #23), and
/// <see cref="AdherenceFigure"/> itself makes the percentage a computed property over its operands'
/// call counts — so FR-33's refusal is two structural facts deep (S-24, issue #38) rather than a
/// runtime check a second client could bypass.</item>
/// <item><see cref="BaseRate"/> — FR-44's conditional-rule figure: a rule that applies only under a
/// condition the logs cannot evaluate (the parallel-tool-calling rule's worked example — a measured
/// 43.6% single-call rate whose availability of a second independent call was never measured). It
/// has no <c>Figure</c> member either, the same as <see cref="General"/>
/// — a base rate is not a resolved adherence percentage — and instead carries a required
/// <c>UnevaluatedCondition</c> stating what could not be checked, so the figure can never render
/// without naming it. Its own <c>"kind"</c> discriminator (<c>"baseRate"</c>) keeps it wire-distinct
/// from <c>"adherence"</c>, which is what makes a base rate visually distinct from a measured
/// violation even when it ranks above one (issue #41, Scenario 2).</item>
/// </list>
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(General), "general")]
[JsonDerivedType(typeof(Adherence), "adherence")]
[JsonDerivedType(typeof(BaseRate), "baseRate")]
public abstract record FindingEnvelope
{
    private FindingEnvelope()
    {
    }

    public required FindingClass Class { get; init; }

    public required Provenance Provenance { get; init; }

    /// <summary>FR-48 (issue #52, S-42): <see cref="Findings.ProvenanceLabel.For"/>'s fixed sentence
    /// for <see cref="Provenance"/>, carried on the wire so the distinguishing text travels with the
    /// finding itself rather than depending on how a client happens to style it — the edge case
    /// named in that story is that styling does not survive being quoted elsewhere.</summary>
    public required string ProvenanceLabel { get; init; }

    public required IReadOnlyList<EvidenceItem> Evidence { get; init; }

    public required Recurrence Recurrence { get; init; }

    /// <summary>FR-56: never a missing field — see <see cref="SuggestionEnvelope"/>.</summary>
    public required SuggestionEnvelope Suggestion { get; init; }

    public required OperatorResponse OperatorResponse { get; init; }

    /// <summary>Assembles the envelope for a finding FR-33 does not apply to.</summary>
    public static General From(Finding finding) => new()
    {
        Class = finding.Class,
        Provenance = finding.Provenance,
        ProvenanceLabel = Findings.ProvenanceLabel.For(finding.Provenance),
        Evidence = finding.Evidence,
        Recurrence = finding.Recurrence,
        Suggestion = SuggestionEnvelope.Of(finding.Suggestion),
        OperatorResponse = finding.OperatorResponse,
    };

    /// <summary>Assembles the envelope for an adherence figure. <paramref name="figure"/> is a
    /// required parameter, not an optional one defaulted from the finding: this is the only method
    /// in this project that produces an <see cref="Adherence"/> envelope, and there is no call to it
    /// that omits the figure (Scenario 3; S-24's Scenario 2). The figure carries its own per-operand
    /// resolution and rule version, so those cannot be supplied out of step with the percentage they
    /// produced.</summary>
    public static Adherence FromAdherence(Finding finding, AdherenceFigure figure) => new()
    {
        Class = finding.Class,
        Provenance = finding.Provenance,
        ProvenanceLabel = Findings.ProvenanceLabel.For(finding.Provenance),
        Evidence = finding.Evidence,
        Recurrence = finding.Recurrence,
        Suggestion = SuggestionEnvelope.Of(finding.Suggestion),
        OperatorResponse = finding.OperatorResponse,
        Figure = figure,
    };

    /// <summary>Assembles the envelope for FR-44's conditional-rule figure.
    /// <paramref name="unevaluatedCondition"/> is a required parameter, the same reasoning
    /// <see cref="FromAdherence"/> gives for <c>resolution</c> and <c>ruleVersion</c>: there is no
    /// call that produces a <see cref="BaseRate"/> envelope without stating what went
    /// unevaluated.</summary>
    public static BaseRate FromBaseRate(Finding finding, string unevaluatedCondition) => new()
    {
        Class = finding.Class,
        Provenance = finding.Provenance,
        ProvenanceLabel = Findings.ProvenanceLabel.For(finding.Provenance),
        Evidence = finding.Evidence,
        Recurrence = finding.Recurrence,
        Suggestion = SuggestionEnvelope.Of(finding.Suggestion),
        OperatorResponse = finding.OperatorResponse,
        UnevaluatedCondition = unevaluatedCondition,
    };

    public sealed record General : FindingEnvelope;

    public sealed record Adherence : FindingEnvelope
    {
        /// <summary>FR-33 (S-24, issue #38): the percentage, the layer that resolved each operand,
        /// the calls each layer produced, and the rule-set version the whole thing was computed
        /// within — one <c>required</c> member, so none of the four can be served without the
        /// others.</summary>
        public required AdherenceFigure Figure { get; init; }
    }

    public sealed record BaseRate : FindingEnvelope
    {
        /// <summary>FR-44, Scenario 1: the condition the logs could not evaluate, stated alongside
        /// the figure rather than left for the reader to infer from the absence of a resolution.
        /// </summary>
        public required string UnevaluatedCondition { get; init; }
    }
}
