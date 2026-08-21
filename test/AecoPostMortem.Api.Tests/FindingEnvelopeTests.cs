using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using AecoPostMortem.Findings;
using AecoPostMortem.Rules;

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
        Headline = "src/foo.cs was read repeatedly",
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
        Headline = "grep was called instead of the preferred tool",
        Evidence = [new EvidenceItem { Field = "data.toolName", Value = "grep" }],
        Recurrence = new Recurrence
        {
            Key = "prefer rg over grep",
            Occurrences = [new RecurrenceOccurrence { SessionId = "session-1", RuleSetVersion = "v3" }],
        },
        Resolution = new Resolution { OperandLayer = "NORMALIZED", CallCount = 12 },
    };

    /// <summary>FR-33's worked shape (S-24, issue #38): "prefer `rg` over shell search", resolved
    /// through two different layers — 3 adherent calls against 1 divergent, so 75%.</summary>
    static AdherenceFigure SampleFigure() => new()
    {
        RuleVersion = new RuleSetVersionId { Repository = "AecoPostMortem", Hash = "b3f1c0" },
        Adherent = new OperandResolution
        {
            OperandText = "rg",
            Layer = OperandResolutionLayer.ExactToolName,
            CallCount = 3,
        },
        Divergent =
        [
            new OperandResolution
            {
                OperandText = "Shell",
                Layer = OperandResolutionLayer.DerivedRole,
                CallCount = 1,
            },
        ],
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
        Headline = "Tool calls were issued one at a time despite the parallel-calling rule",
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

    /// <summary>Mockup parity item #5: the same structural guarantee <see cref="Provenance"/> already
    /// gets on every shape, extended to the headline field this story adds.</summary>
    [Theory]
    [InlineData(typeof(FindingEnvelope.General))]
    [InlineData(typeof(FindingEnvelope.Adherence))]
    [InlineData(typeof(FindingEnvelope.BaseRate))]
    public void Headline_is_a_required_member_on_every_shape(Type envelopeType)
    {
        var property = envelopeType.GetProperty(nameof(FindingEnvelope.Headline));

        Assert.NotNull(property);
        Assert.NotNull(property!.GetCustomAttribute<RequiredMemberAttribute>());
    }

    /// <summary>Mockup parity item #5: the served headline is the finding's own, passed straight
    /// through — this project computes nothing new.</summary>
    [Fact]
    public void The_headline_is_passed_through_from_the_finding_unchanged()
    {
        var envelope = FindingEnvelope.From(SampleWasteFinding());

        Assert.Equal(SampleWasteFinding().Headline, envelope.Headline);
    }

    [Fact]
    public void The_general_shape_has_no_figure_resolution_or_rule_version_members()
    {
        // A finding that is not an adherence figure is served through this shape, which structurally
        // cannot carry a figure, resolution or rule version at all — there is no field to leave null.
        Assert.Null(typeof(FindingEnvelope.General).GetProperty("Figure"));
        Assert.Null(typeof(FindingEnvelope.General).GetProperty("Resolution"));
        Assert.Null(typeof(FindingEnvelope.General).GetProperty("RuleVersion"));
    }

    /// <summary>S-24 / FR-33, Scenario 2: the adherence shape carries exactly one member — the
    /// figure — and it is <c>required</c>, so assembling one without it is CS9035. The figure itself
    /// (<see cref="AdherenceFigure"/>) is what makes the percentage inseparable from the resolution
    /// and rule version, rather than the envelope repeating three fields a caller could pair
    /// wrongly.</summary>
    [Fact]
    public void The_figure_is_a_required_member_on_the_adherence_shape()
    {
        var property = typeof(FindingEnvelope.Adherence).GetProperty(nameof(FindingEnvelope.Adherence.Figure));

        Assert.NotNull(property);
        Assert.NotNull(property!.GetCustomAttribute<RequiredMemberAttribute>());
    }

    /// <summary>The refusal has to be structural rather than a runtime check a second client could
    /// bypass (issue #38's edge case). <c>FromAdherence</c> is the only producer of this shape, and
    /// it takes the figure as a required parameter — there is no overload, no parameterless
    /// constructor and no other public factory that could yield an adherence envelope without
    /// one.</summary>
    [Fact]
    public void The_only_way_to_produce_an_adherence_envelope_takes_the_figure_as_a_required_parameter()
    {
        var producers = typeof(FindingEnvelope)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => typeof(FindingEnvelope.Adherence).IsAssignableFrom(method.ReturnType))
            .ToArray();

        var producer = Assert.Single(producers);
        Assert.Equal(nameof(FindingEnvelope.FromAdherence), producer.Name);
        Assert.Contains(
            producer.GetParameters(),
            parameter => parameter.ParameterType == typeof(AdherenceFigure) && !parameter.IsOptional);

    }

    /// <summary>The one escape hatch that would let <c>new FindingEnvelope.Adherence()</c> compile
    /// despite a <c>required</c> member is a constructor marked <c>[SetsRequiredMembers]</c>, which
    /// tells the compiler to stop enforcing CS9035. No constructor on the adherence shape — or on
    /// the figure it carries — may claim it, or the refusal stops being structural and becomes a
    /// convention a second client could walk past. A record's own synthesized copy constructor
    /// (the one taking the record type itself, behind every <c>with</c> expression) carries the
    /// attribute by design — it copies members that are already set — so it is excluded here; every
    /// other constructor is the kind that could hand out an unset figure.</summary>
    [Theory]
    [InlineData(typeof(FindingEnvelope.Adherence))]
    [InlineData(typeof(AdherenceFigure))]
    [InlineData(typeof(OperandResolution))]
    public void No_constructor_opts_out_of_required_member_enforcement(Type type)
    {
        var constructors = type
            .GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(constructor =>
            {
                var parameters = constructor.GetParameters();
                return parameters.Length != 1 || !parameters[0].ParameterType.IsAssignableFrom(type);
            })
            .ToArray();

        Assert.NotEmpty(constructors);
        Assert.All(
            constructors,
            constructor => Assert.Null(constructor.GetCustomAttribute<SetsRequiredMembersAttribute>()));
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
    public void FromAdherence_assembles_the_adherence_shape_only_given_a_figure()
    {
        var envelope = FindingEnvelope.FromAdherence(SampleAdherenceFinding(), SampleFigure());

        Assert.Equal(FindingClass.RuleAdherenceToolChoice, envelope.Class);
        Assert.Equal(75d, envelope.Figure.Percentage);
        Assert.Equal("b3f1c0", envelope.Figure.RuleVersion.Hash);
        Assert.Equal(2, envelope.Figure.Operands.Count);
    }

    [Fact]
    public void FindingEnvelope_serialises_with_a_kind_discriminator_distinguishing_the_two_shapes()
    {
        FindingEnvelope adherence = FindingEnvelope.FromAdherence(SampleAdherenceFinding(), SampleFigure());

        var json = JsonSerializer.Serialize(adherence);
        using var document = JsonDocument.Parse(json);

        Assert.True(document.RootElement.TryGetProperty("kind", out var kind));
        Assert.Equal("adherence", kind.GetString());

        var roundTripped = JsonSerializer.Deserialize<FindingEnvelope>(json);
        var adherenceRoundTripped = Assert.IsType<FindingEnvelope.Adherence>(roundTripped);
        Assert.Equal("b3f1c0", adherenceRoundTripped.Figure.RuleVersion.Hash);
    }

    /// <summary>S-24 / FR-33, Scenario 2 read at the wire rather than at the type: whenever a served
    /// adherence figure states a percentage, the same JSON object states every operand's layer, that
    /// operand's call count, and the rule version. There is no serialised form of this shape in
    /// which the percentage appears alone, because all three ride on one required member.</summary>
    [Fact]
    public void A_served_adherence_figure_never_states_a_percentage_without_its_operands_and_rule_version()
    {
        FindingEnvelope adherence = FindingEnvelope.FromAdherence(SampleAdherenceFinding(), SampleFigure());

        var json = JsonSerializer.Serialize(adherence);
        using var document = JsonDocument.Parse(json);

        var figure = document.RootElement.GetProperty("Figure");

        Assert.Equal(75d, figure.GetProperty("Percentage").GetDouble());
        Assert.Equal("b3f1c0", figure.GetProperty("RuleVersion").GetProperty("Hash").GetString());

        var operands = figure.GetProperty("Operands").EnumerateArray().ToArray();
        Assert.Equal(2, operands.Length);
        Assert.All(operands, operand =>
        {
            Assert.False(string.IsNullOrWhiteSpace(operand.GetProperty("OperandText").GetString()));
            Assert.True(operand.TryGetProperty("Layer", out _));
            Assert.True(operand.GetProperty("CallCount").GetInt32() >= 0);
        });
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
