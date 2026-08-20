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

    // FR-44's worked example: the parallel-tool-calling rule measured a 43.6% single-call rate
    // across 7,449 tool-issuing messages (3,249 of them), and whether a second independent call
    // was available at each point was never measured — presenting that as disobedience is the
    // exact failure PRD §3.9 lists. Provenance is Inferred, not Observed: the count itself is a
    // plain fact, but treating it as bearing on the rule at all assumes the unmeasured condition
    // held.
    static Finding SampleConditionalRuleFinding() => new()
    {
        Class = FindingClass.RuleAdherenceToolChoice,
        Provenance = Provenance.Inferred,
        Evidence =
        [
            new EvidenceItem { Field = "single_call_messages", Value = "3249" },
            new EvidenceItem { Field = "tool_issuing_messages", Value = "7449" },
        ],
        Recurrence = new Recurrence
        {
            Key = "USE PARALLEL TOOL CALLING — when you need to perform multiple independent operations, make ALL tool calls in a SINGLE response",
            Occurrences = [new RecurrenceOccurrence { SessionId = "session-1" }],
        },
    };

    const string ParallelCallAvailabilityUnevaluated =
        "whether a second independent call was available at each point was never measured";

    [Theory]
    [InlineData(typeof(FindingEnvelope.General))]
    [InlineData(typeof(FindingEnvelope.Adherence))]
    [InlineData(typeof(FindingEnvelope.BaseRate))]
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

    // FR-44, Scenario 1 ("A conditional rule is labelled as a base rate"): the base-rate shape has
    // no Resolution or RuleVersion members at all — the same structural move General already makes
    // — so a conditional rule's figure can never be assembled as though it were a resolved
    // adherence percentage.
    [Fact]
    public void The_base_rate_shape_has_no_resolution_or_rule_version_members()
    {
        Assert.Null(typeof(FindingEnvelope.BaseRate).GetProperty("Resolution"));
        Assert.Null(typeof(FindingEnvelope.BaseRate).GetProperty("RuleVersion"));
    }

    [Fact]
    public void UnevaluatedCondition_is_a_required_member_on_the_base_rate_shape()
    {
        var property = typeof(FindingEnvelope.BaseRate).GetProperty(nameof(FindingEnvelope.BaseRate.UnevaluatedCondition));

        Assert.NotNull(property);
        Assert.NotNull(property!.GetCustomAttribute<RequiredMemberAttribute>());
    }

    [Fact]
    public void FromBaseRate_states_the_unevaluated_condition_alongside_the_measured_figure()
    {
        var finding = SampleConditionalRuleFinding();

        var envelope = FindingEnvelope.FromBaseRate(finding, ParallelCallAvailabilityUnevaluated);

        Assert.Equal(FindingClass.RuleAdherenceToolChoice, envelope.Class);
        Assert.Equal(Provenance.Inferred, envelope.Provenance);
        Assert.Equal(ParallelCallAvailabilityUnevaluated, envelope.UnevaluatedCondition);
        Assert.Contains(envelope.Evidence, item => item.Field == "single_call_messages" && item.Value == "3249");
        Assert.Contains(envelope.Evidence, item => item.Field == "tool_issuing_messages" && item.Value == "7449");
    }

    // FR-44, Scenario 2 ("A base rate is never ranked as a violation"): the wire discriminator is
    // the visual/structural distinction a client renders on — "baseRate" can never collide with
    // "adherence", the shape a measured violation like the navigation-rule finding uses.
    [Fact]
    public void FindingEnvelope_serialises_the_base_rate_kind_distinct_from_adherence_and_general()
    {
        FindingEnvelope baseRate = FindingEnvelope.FromBaseRate(SampleConditionalRuleFinding(), ParallelCallAvailabilityUnevaluated);

        var json = JsonSerializer.Serialize(baseRate);
        using var document = JsonDocument.Parse(json);

        Assert.True(document.RootElement.TryGetProperty("kind", out var kind));
        Assert.Equal("baseRate", kind.GetString());
        Assert.NotEqual("adherence", kind.GetString());
        Assert.NotEqual("general", kind.GetString());

        var roundTripped = JsonSerializer.Deserialize<FindingEnvelope>(json);
        var baseRateRoundTripped = Assert.IsType<FindingEnvelope.BaseRate>(roundTripped);
        Assert.Equal(ParallelCallAvailabilityUnevaluated, baseRateRoundTripped.UnevaluatedCondition);
    }
}
