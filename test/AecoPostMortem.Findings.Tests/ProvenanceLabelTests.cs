namespace AecoPostMortem.Findings.Tests;

/// <summary>
/// FR-48's second scenario (issue #52, S-42): the three provenance levels have to be
/// distinguishable without relying on colour, and an Inferred finding has to read as a hypothesis
/// in its own words — not only in a CSS class or an icon, since (per the story's own edge case)
/// styling does not survive being quoted elsewhere. <see cref="ProvenanceLabel.For"/> is the one
/// place that text lives: a fixed sentence per <see cref="Provenance"/> value, carried on every
/// served finding (<see cref="Api.FindingEnvelope"/>) so the distinguishing signal travels with the
/// finding itself, not with wherever it happens to be displayed.
/// </summary>
public sealed class ProvenanceLabelTests
{
    [Theory]
    [InlineData(Provenance.Observed)]
    [InlineData(Provenance.Derived)]
    [InlineData(Provenance.Inferred)]
    public void Every_provenance_level_has_a_non_empty_label(Provenance provenance)
    {
        Assert.False(string.IsNullOrWhiteSpace(ProvenanceLabel.For(provenance)));
    }

    [Fact]
    public void The_three_labels_are_textually_distinct()
    {
        var labels = Enum.GetValues<Provenance>().Select(ProvenanceLabel.For).ToArray();

        Assert.Equal(labels.Length, labels.Distinct().Count());
    }

    [Fact]
    public void Only_the_inferred_label_reads_as_a_hypothesis()
    {
        Assert.Contains("hypothesis", ProvenanceLabel.For(Provenance.Inferred), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hypothesis", ProvenanceLabel.For(Provenance.Observed), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hypothesis", ProvenanceLabel.For(Provenance.Derived), StringComparison.OrdinalIgnoreCase);
    }
}
