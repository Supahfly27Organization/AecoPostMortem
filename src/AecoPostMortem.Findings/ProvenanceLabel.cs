namespace AecoPostMortem.Findings;

/// <summary>
/// FR-48's own words for each <see cref="Provenance"/> level (issue #52, S-42): a fixed sentence
/// per level, distinguishable from the other two by its wording alone. The words themselves are the
/// distinguishing signal, not a colour, icon or CSS class — the story's own edge case is that
/// styling does not survive being quoted elsewhere, so an Inferred finding's label names it a
/// hypothesis in its own text, not only in how it happens to be rendered. This is a text-rendering
/// of the existing <see cref="Provenance"/> enum, not a new domain concept: there is nothing here a
/// caller could not already derive from the enum value, only the human-readable form of it.
/// </summary>
public static class ProvenanceLabel
{
    public static string For(Provenance provenance) => provenance switch
    {
        Provenance.Observed => "Observed — read directly from the session log.",
        Provenance.Derived => "Derived — computed from observed session data.",
        Provenance.Inferred => "Hypothesis — inferred, not observed.",
        _ => throw new ArgumentOutOfRangeException(nameof(provenance), provenance, message: null),
    };
}
