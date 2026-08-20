using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using AecoPostMortem.Findings;

namespace AecoPostMortem.Api.Tests;

/// <summary>
/// Scenarios 1 and 3 of the API response envelope (issue #13): every served finding carries
/// provenance, and an adherence figure cannot be represented without its resolution and rule
/// version. Both are structural — a compile-time <c>required</c> member, the same guarantee
/// <c>Finding.Provenance</c> already gives (issue #23) — not a runtime check.
/// </summary>
public sealed class FindingEnvelopeTests
{
    static Finding SampleWasteFinding() => new()
    {
        Class = FindingClass.Waste,
        Provenance = Provenance.Derived,
        Evidence = [new EvidenceItem { Field = "data.path", Value = "src/foo.cs" }],
        Recurrence = new Recurrence
        {
            Key = "src/foo.cs",
            Occurrences = [new RecurrenceOccurrence { SessionId = "session-1" }],
        },
    };

    static Finding SampleAdherenceFinding() => new()
    {
        Class = FindingClass.RuleAdherenceToolChoice,
        Provenance = Provenance.Derived,
        Evidence = [new EvidenceItem { Field = "data.toolName", Value = "grep" }],
        Recurrence = new Recurrence
        {
            Key = "prefer rg over grep",
            Occurrences = [new RecurrenceOccurrence { SessionId = "session-1", RuleSetVersion = "v3" }],
        },
        Resolution = new Resolution { OperandLayer = "NORMALIZED", CallCount = 12 },
    };

    [Theory]
    [InlineData(typeof(FindingEnvelope.General))]
    [InlineData(typeof(FindingEnvelope.Adherence))]
    public void Provenance_is_a_required_member_on_every_shape(Type envelopeType)
    {
        var property = envelopeType.GetProperty(nameof(FindingEnvelope.Provenance));

        Assert.NotNull(property);
        Assert.NotNull(property!.GetCustomAttribute<RequiredMemberAttribute>());
    }

    [Fact]
    public void The_general_shape_has_no_resolution_or_rule_version_members()
    {
        // A finding that is not an adherence figure is served through this shape, which structurally
        // cannot carry a resolution or rule version at all — there is no field to leave null.
        Assert.Null(typeof(FindingEnvelope.General).GetProperty("Resolution"));
        Assert.Null(typeof(FindingEnvelope.General).GetProperty("RuleVersion"));
    }

    [Theory]
    [InlineData("Resolution")]
    [InlineData("RuleVersion")]
    public void Resolution_and_rule_version_are_required_members_on_the_adherence_shape(string propertyName)
    {
        var property = typeof(FindingEnvelope.Adherence).GetProperty(propertyName);

        Assert.NotNull(property);
        Assert.NotNull(property!.GetCustomAttribute<RequiredMemberAttribute>());
    }

    [Fact]
    public void From_maps_a_non_adherence_finding_to_the_general_shape()
    {
        var envelope = FindingEnvelope.From(SampleWasteFinding());

        var general = Assert.IsType<FindingEnvelope.General>(envelope);
        Assert.Equal(FindingClass.Waste, general.Class);
        Assert.Equal(Provenance.Derived, general.Provenance);
        Assert.Same(SuggestionEnvelope.Absent, general.Suggestion);
    }

    [Fact]
    public void FromAdherence_assembles_the_adherence_shape_only_given_resolution_and_rule_version()
    {
        var finding = SampleAdherenceFinding();

        var envelope = FindingEnvelope.FromAdherence(finding, finding.Resolution!, ruleVersion: "v3");

        Assert.Equal(FindingClass.RuleAdherenceToolChoice, envelope.Class);
        Assert.Equal(12, envelope.Resolution.CallCount);
        Assert.Equal("v3", envelope.RuleVersion);
    }

    [Fact]
    public void FindingEnvelope_serialises_with_a_kind_discriminator_distinguishing_the_two_shapes()
    {
        FindingEnvelope adherence = FindingEnvelope.FromAdherence(
            SampleAdherenceFinding(), new Resolution { OperandLayer = "NORMALIZED", CallCount = 12 }, "v3");

        var json = JsonSerializer.Serialize(adherence);
        using var document = JsonDocument.Parse(json);

        Assert.True(document.RootElement.TryGetProperty("kind", out var kind));
        Assert.Equal("adherence", kind.GetString());

        var roundTripped = JsonSerializer.Deserialize<FindingEnvelope>(json);
        var adherenceRoundTripped = Assert.IsType<FindingEnvelope.Adherence>(roundTripped);
        Assert.Equal("v3", adherenceRoundTripped.RuleVersion);
    }

    /// <summary>FR-48 (issue #52, S-42): the provenance label is required on every shape, exactly
    /// the guarantee <see cref="Provenance_is_a_required_member_on_every_shape"/> gives
    /// <c>Provenance</c> itself — a served finding cannot omit the text that distinguishes it.
    /// </summary>
    [Theory]
    [InlineData(typeof(FindingEnvelope.General))]
    [InlineData(typeof(FindingEnvelope.Adherence))]
    public void ProvenanceLabel_is_a_required_member_on_every_shape(Type envelopeType)
    {
        var property = envelopeType.GetProperty(nameof(FindingEnvelope.ProvenanceLabel));

        Assert.NotNull(property);
        Assert.NotNull(property!.GetCustomAttribute<RequiredMemberAttribute>());
    }

    /// <summary>The edge case named in issue #52: a hypothesis has to read as one in its own text,
    /// since styling does not survive being quoted elsewhere — an Inferred finding's served label
    /// names it a hypothesis, and the other two levels' labels do not.</summary>
    [Fact]
    public void An_inferred_findings_served_label_reads_as_a_hypothesis()
    {
        var finding = SampleWasteFinding() with { Provenance = Provenance.Inferred };

        var envelope = FindingEnvelope.From(finding);

        Assert.Contains("hypothesis", envelope.ProvenanceLabel, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_observed_or_derived_findings_served_label_does_not_read_as_a_hypothesis()
    {
        var envelope = FindingEnvelope.From(SampleWasteFinding());

        Assert.DoesNotContain("hypothesis", envelope.ProvenanceLabel, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_provenance_label_survives_serialisation_independent_of_any_styling()
    {
        var finding = SampleWasteFinding() with { Provenance = Provenance.Inferred };
        var envelope = FindingEnvelope.From(finding);

        var json = JsonSerializer.Serialize<FindingEnvelope>(envelope);
        using var document = JsonDocument.Parse(json);

        Assert.Contains(
            "hypothesis",
            document.RootElement.GetProperty("ProvenanceLabel").GetString(),
            StringComparison.OrdinalIgnoreCase);
    }
}
