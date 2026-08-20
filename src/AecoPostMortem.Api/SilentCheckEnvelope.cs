using AecoPostMortem.Findings;

namespace AecoPostMortem.Api;

/// <summary>
/// FR-42: the "checks that found nothing" surface. States a silent check's identity, the population
/// it ran over, and that it found nothing — the denominator that keeps "clean" distinguishable from
/// "never looked" (PRD §3.9).
/// </summary>
/// <remarks>
/// <see cref="From"/> is the only producer: it projects a <see cref="CheckRegistry"/> down to the
/// entries that actually ran clean (<see cref="CheckRunStatus.Ran"/> with a zero
/// <see cref="CheckRegistryEntry.FindingCount"/>), which is what makes the three failure modes this
/// story exists to keep apart hold structurally rather than by caller discipline:
/// <list type="bullet">
/// <item>A <see cref="CheckRunStatus.Refused"/> entry is filtered out here — a refused check belongs
/// to the Rules Inventory (FR-53) as "not checkable", never shown on this surface as a false
/// clean (Scenario 4).</item>
/// <item>A check the registry carries no entry for at all — not built yet in this release — has
/// nothing for <see cref="From"/> to project, so it is simply absent rather than implied clean
/// (Scenario 3). Nothing here synthesises a placeholder entry.</item>
/// <item>A check that ran and found something (<see cref="CheckRegistryEntry.FindingCount"/> &gt; 0)
/// is also excluded: it is reported through the finding surface, not implied "found nothing" here.</item>
/// </list>
/// </remarks>
public sealed record SilentCheckEnvelope
{
    public required string CheckId { get; init; }

    /// <summary>The candidate set the check ran over — sessions or lines, per FR-42.</summary>
    public required int Population { get; init; }

    /// <summary>Always <c>0</c> on this surface: <see cref="From"/> only projects entries that ran
    /// clean. Carried explicitly, rather than left implicit, so a served entry states outright that
    /// it found nothing instead of asking the reader to infer it from appearing here at all.</summary>
    public required int FindingCount { get; init; }

    /// <summary>Mockup parity item #6's provenance badge (`docs/product-superpowers/discovery/
    /// mockups/digest.html`'s `.ck` card): the provenance the check would have produced, projected
    /// straight from <see cref="CheckRegistryEntry.Provenance"/> — a fixed, caller-stated fact, never
    /// derived here.</summary>
    public required Provenance Provenance { get; init; }

    /// <summary>The same fixed sentence per level <see cref="FindingEnvelope.ProvenanceLabel"/> already
    /// serves (FR-48) — reused verbatim via <see cref="Findings.ProvenanceLabel.For"/> rather than a
    /// second wording table, so a clean check's badge and a finding's badge never disagree on the words.
    /// </summary>
    public required string ProvenanceLabel { get; init; }

    public static IReadOnlyList<SilentCheckEnvelope> From(CheckRegistry registry) =>
        registry.Entries
            .Where(entry => entry.Status == CheckRunStatus.Ran && entry.FindingCount == 0)
            .Select(entry => new SilentCheckEnvelope
            {
                CheckId = entry.CheckId,
                Population = entry.Population,
                FindingCount = entry.FindingCount!.Value,
                Provenance = entry.Provenance,
                ProvenanceLabel = Findings.ProvenanceLabel.For(entry.Provenance),
            })
            .ToList();
}
