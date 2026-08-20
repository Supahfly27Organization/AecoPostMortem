using System.Text.Json.Serialization;
using AecoPostMortem.Findings;

namespace AecoPostMortem.Api;

/// <summary>
/// FR-59: the API response contract for one served finding. Provenance is <c>required</c> on the
/// base type, so every shape carries it (Scenario 1). Two closed shapes exist beneath it — the
/// private constructor restricts derivation to the nested <see cref="General"/> and
/// <see cref="Adherence"/> records, the only types with access to it:
/// <list type="bullet">
/// <item><see cref="General"/> — every finding class FR-33 does not apply to. It has no
/// <c>Resolution</c> or <c>RuleVersion</c> members at all; there is no field on this shape a bare
/// adherence figure could leave null.</item>
/// <item><see cref="Adherence"/> — the only shape that can carry a resolution, and the only shape
/// where <c>Resolution</c> and <c>RuleVersion</c> are <c>required</c>. Assembling one without both is
/// a compile error (CS9035), the same guarantee <c>Finding.Provenance</c> already gives (issue
/// #23) — FR-33's refusal lives here structurally; S-24 exercises the resulting behaviour at the API
/// boundary.</item>
/// </list>
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(General), "general")]
[JsonDerivedType(typeof(Adherence), "adherence")]
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

    /// <summary>Assembles the envelope for an adherence figure. <paramref name="resolution"/> and
    /// <paramref name="ruleVersion"/> are required parameters, not optional ones defaulted from the
    /// finding: there is no call that produces an <see cref="Adherence"/> envelope without supplying
    /// both (Scenario 3).</summary>
    public static Adherence FromAdherence(Finding finding, Resolution resolution, string ruleVersion) => new()
    {
        Class = finding.Class,
        Provenance = finding.Provenance,
        ProvenanceLabel = Findings.ProvenanceLabel.For(finding.Provenance),
        Evidence = finding.Evidence,
        Recurrence = finding.Recurrence,
        Suggestion = SuggestionEnvelope.Of(finding.Suggestion),
        OperatorResponse = finding.OperatorResponse,
        Resolution = resolution,
        RuleVersion = ruleVersion,
    };

    public sealed record General : FindingEnvelope;

    public sealed record Adherence : FindingEnvelope
    {
        public required Resolution Resolution { get; init; }

        public required string RuleVersion { get; init; }
    }
}
