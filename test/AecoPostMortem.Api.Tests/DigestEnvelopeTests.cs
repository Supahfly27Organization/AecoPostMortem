using System.Text.Json;
using AecoPostMortem.Findings;
using AecoPostMortem.Rules;

namespace AecoPostMortem.Api.Tests;

/// <summary>
/// FR-41's served digest (issue #44, S-36): the wire shape a client reads the masthead and the
/// ranked findings through, assembled from <see cref="ProcessDigest"/> the same way
/// <see cref="FindingEnvelope"/> is assembled from a <c>Finding</c> (S-50, issue #13).
/// </summary>
public sealed class DigestEnvelopeTests
{
    static Finding WasteFinding(string path, params string[] sessionIds) => new()
    {
        Class = FindingClass.Waste,
        Provenance = Provenance.Derived,
        Evidence = [new EvidenceItem { Field = "data.path", Value = path }],
        Recurrence = new Recurrence
        {
            Key = path,
            Occurrences = [.. sessionIds.Select(id => new RecurrenceOccurrence { SessionId = id })],
        },
    };

    // FR-33's measured violation: an adherence figure with its resolution, contrasted below
    // against FR-44's base rate over more sessions — the ranking (by sessions affected) would put
    // the base rate first, which is exactly why its wire shape has to stay visually distinct from
    // an actual violation regardless of rank.
    static Finding AdherenceFinding(string ruleStatement, params string[] sessionIds) => new()
    {
        Class = FindingClass.RuleAdherenceToolChoice,
        Provenance = Provenance.Derived,
        Evidence = [new EvidenceItem { Field = "data.toolName", Value = "grep" }],
        Recurrence = new Recurrence
        {
            Key = ruleStatement,
            Occurrences = [.. sessionIds.Select(id => new RecurrenceOccurrence { SessionId = id })],
        },
        Resolution = new Resolution { OperandLayer = "NORMALIZED", CallCount = 12 },
    };

    /// <summary>FR-33 (S-24, issue #38): the figure the adherence shape is served through — the
    /// percentage inseparable from the layer that resolved each operand and the calls it produced.
    /// </summary>
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

    // FR-44's worked example, mirroring FindingEnvelopeTests: the parallel-tool-calling rule's
    // 43.6% single-call rate depends on an unmeasured condition, so it is Inferred, not Observed.
    static Finding ConditionalRuleFinding(params string[] sessionIds) => new()
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
            Occurrences = [.. sessionIds.Select(id => new RecurrenceOccurrence { SessionId = id })],
        },
    };

    const string ParallelCallAvailabilityUnevaluated =
        "whether a second independent call was available at each point was never measured";

    static MastheadCounters Counters() => new()
    {
        SessionCount = 35,
        SpanStart = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero),
        SpanEnd = new DateTimeOffset(2026, 8, 19, 0, 0, 0, TimeSpan.Zero),
        RepositoryCount = 3,
        EventCount = 56_138,
        ToolCallCount = 12_345,
        IngestInProgress = false,
    };

    static CheckRegistry RanCleanRegistry() => new()
    {
        Entries =
        [
            new CheckRegistryEntry
            {
                CheckId = "repeated-file-read",
                Status = CheckRunStatus.Ran,
                Population = 35,
                FindingCount = 1,
            },
        ],
    };

    static RepositoryScope SingleRepoScope() => new()
    {
        SelectedRepository = "aeco/AecoPostMortem",
        AvailableRepositories = ["aeco/AecoPostMortem"],
    };

    static Finding InferredFinding(string toolName, params string[] sessionIds) => new()
    {
        Class = FindingClass.MissingCapability,
        Provenance = Provenance.Inferred,
        Evidence = [new EvidenceItem { Field = "data.toolName", Value = toolName }],
        Recurrence = new Recurrence
        {
            Key = toolName,
            Occurrences = [.. sessionIds.Select(id => new RecurrenceOccurrence { SessionId = id })],
        },
    };

    /// <summary>FR-48 (issue #52, S-42): the served digest carries Inferred findings in their own
    /// section, never inside <c>RankedFindings</c> — the same separation
    /// <see cref="Findings.ProcessDigest"/> already draws, just mapped to the wire shape.</summary>
    [Fact]
    public void InferredFindings_are_served_separately_from_the_ranked_list()
    {
        var digest = ProcessDigest.Build(
            Counters(),
            RanCleanRegistry(),
            [WasteFinding("src/hot.cs", "session-1"), InferredFinding("web_fetch", "session-1")],
            SingleRepoScope());

        var envelope = DigestEnvelope.From(digest, FindingEnvelope.From);

        Assert.Single(envelope.RankedFindings);
        Assert.DoesNotContain(envelope.RankedFindings, f => f.Provenance == Provenance.Inferred);

        Assert.Single(envelope.InferredFindings);
        Assert.Equal(Provenance.Inferred, envelope.InferredFindings[0].Provenance);
        Assert.Equal("web_fetch", envelope.InferredFindings[0].Recurrence.Key);
    }

    /// <summary>S-36's edge case says a finding touching one session must read as an anecdote beside
    /// one touching thirty, which makes "how many sessions this touched" the most prominent figure on
    /// a rendered row. That number is served rather than left for each client to re-derive from
    /// <c>Recurrence.Occurrences</c>: it is the key <see cref="ProcessDigest.Build"/> ordered the list
    /// by, so a client deriving its own copy could silently disagree with the very order it is
    /// rendering. Distinct sessions, not raw occurrences — <c>session-2</c> appears twice below and
    /// counts once, the same rule <see cref="ProcessDigest.SessionsAffected"/> applies when ranking.
    /// </summary>
    [Fact]
    public void Every_served_finding_carries_the_sessions_affected_count_it_was_ranked_by()
    {
        var digest = ProcessDigest.Build(
            Counters(),
            RanCleanRegistry(),
            [
                WasteFinding("src/hot.cs", "session-1", "session-2", "session-2"),
                WasteFinding("src/rare.cs", "session-9"),
            ],
            SingleRepoScope());

        var envelope = DigestEnvelope.From(digest, FindingEnvelope.From);

        Assert.Equal([2, 1], envelope.RankedFindings.Select(finding => finding.SessionsAffected));
    }

    [Fact]
    public void From_carries_the_digest_state_and_maps_every_ranked_finding_in_order()
    {
        var digest = ProcessDigest.Build(
            Counters(),
            RanCleanRegistry(),
            [WasteFinding("src/rare.cs", "session-1"), WasteFinding("src/hot.cs", "session-1", "session-2")],
            SingleRepoScope());

        var envelope = DigestEnvelope.From(digest, FindingEnvelope.From);

        Assert.Equal(DigestState.Analyzed, envelope.State);
        Assert.Equal(2, envelope.RankedFindings.Count);
        Assert.Equal(
            ["src/hot.cs", "src/rare.cs"],
            envelope.RankedFindings.Select(f => f.Recurrence.Key));
    }

    [Fact]
    public void The_masthead_envelope_carries_sessions_span_repositories_events_tool_calls_and_rule_coverage()
    {
        var digest = ProcessDigest.Build(Counters(), RanCleanRegistry(), [], SingleRepoScope());

        var envelope = DigestEnvelope.From(digest, FindingEnvelope.From);

        Assert.Equal(35, envelope.Masthead.SessionCount);
        Assert.NotNull(envelope.Masthead.SpanStart);
        Assert.NotNull(envelope.Masthead.SpanEnd);
        Assert.Equal(3, envelope.Masthead.RepositoryCount);
        Assert.Equal(56_138, envelope.Masthead.EventCount);
        Assert.Equal(12_345, envelope.Masthead.ToolCallCount);
        Assert.Equal(RuleCoverageStatus.NotYetAnalyzed, envelope.Masthead.RuleCoverage);
    }

    [Fact]
    public void The_masthead_envelope_carries_the_selected_repository_and_the_ones_available_to_switch_to()
    {
        var scope = new RepositoryScope
        {
            SelectedRepository = "aeco/AecoPostMortem",
            AvailableRepositories = ["aeco/AecoLedger", "aeco/AecoPostMortem", "aeco/Upfront"],
        };

        var digest = ProcessDigest.Build(Counters(), RanCleanRegistry(), [], scope);

        var envelope = DigestEnvelope.From(digest, FindingEnvelope.From);

        Assert.Equal("aeco/AecoPostMortem", envelope.Masthead.RepositoryScope.SelectedRepository);
        Assert.Equal(
            ["aeco/AecoLedger", "aeco/AecoPostMortem", "aeco/Upfront"],
            envelope.Masthead.RepositoryScope.AvailableRepositories);
    }

    [Fact]
    public void DigestEnvelope_serialises_the_state_and_the_ranked_findings()
    {
        var digest = ProcessDigest.Build(
            Counters(), RanCleanRegistry(), [WasteFinding("src/hot.cs", "session-1")], SingleRepoScope());
        var envelope = DigestEnvelope.From(digest, FindingEnvelope.From);

        var json = JsonSerializer.Serialize(envelope);
        using var document = JsonDocument.Parse(json);

        Assert.Equal("Analyzed", document.RootElement.GetProperty("State").GetString());
        Assert.Equal(1, document.RootElement.GetProperty("RankedFindings").GetArrayLength());

        var masthead = document.RootElement.GetProperty("Masthead");
        Assert.True(masthead.TryGetProperty("SessionCount", out _));
        Assert.Equal("NotYetAnalyzed", masthead.GetProperty("RuleCoverage").GetString());
        Assert.Equal(
            "aeco/AecoPostMortem",
            masthead.GetProperty("RepositoryScope").GetProperty("SelectedRepository").GetString());
    }

    [Fact]
    public void A_row_with_no_suggestion_template_still_serialises_an_explicit_absent_suggestion_state()
    {
        // Scenario 4: a finding whose class has no suggestion template expands with its evidence and
        // states that no suggestion is offered — reusing SuggestionEnvelope's existing Absent state
        // (S-50, issue #13) rather than a new "no suggestion" representation.
        var noSuggestion = WasteFinding("src/no-template.cs", "session-1");
        var digest = ProcessDigest.Build(Counters(), RanCleanRegistry(), [noSuggestion], SingleRepoScope());

        var envelope = DigestEnvelope.From(digest, FindingEnvelope.From);

        var row = Assert.Single(envelope.RankedFindings);
        Assert.IsType<SuggestionEnvelope.AbsentSuggestion>(row.Suggestion);

        var json = JsonSerializer.Serialize(envelope);
        using var document = JsonDocument.Parse(json);
        var suggestion = document.RootElement.GetProperty("RankedFindings")[0].GetProperty("Suggestion");
        Assert.Equal("absent", suggestion.GetProperty("state").GetString());
    }

    [Fact]
    public void An_empty_store_serialises_as_not_yet_analyzed()
    {
        var digest = ProcessDigest.Build(
            new MastheadCounters
            {
                SessionCount = 0,
                SpanStart = null,
                SpanEnd = null,
                RepositoryCount = 0,
                EventCount = 0,
                ToolCallCount = 0,
                IngestInProgress = false,
            },
            new CheckRegistry { Entries = [] },
            [],
            new RepositoryScope { SelectedRepository = null, AvailableRepositories = [] });

        var envelope = DigestEnvelope.From(digest, FindingEnvelope.From);

        Assert.Equal(DigestState.NotYetAnalyzed, envelope.State);
        Assert.Empty(envelope.RankedFindings);
    }

    // FR-44, Scenario 2 ("A base rate is never ranked as a violation") and FR-48, Scenario 1
    // ("Inferred findings are not in the ranked list"): a base rate carries Provenance.Inferred
    // (FindingEnvelopeTests' own worked example), so ProcessDigest.Build's FR-48 partition already
    // keeps it out of RankedFindings structurally — the same session count that would rank it above
    // a measured violation never gets the chance to, because it is never in that list at all. This
    // is a strictly stronger reading of "never ranked as a violation" than a same-list, distinct-kind
    // compromise: FR-44's own wire-discriminator guarantee (BaseRate vs. Adherence) still holds for a
    // client reading InferredFindings, on top of the list-level separation FR-48 adds.
    [Fact]
    public void A_base_rate_item_never_appears_in_ranked_findings_and_serialises_a_distinct_kind_in_inferred_findings()
    {
        var digest = ProcessDigest.Build(
            Counters(),
            RanCleanRegistry(),
            [
                AdherenceFinding("prefer rg over grep", "session-1"),
                ConditionalRuleFinding("session-1", "session-2", "session-3"),
            ],
            SingleRepoScope());

        FindingEnvelope MapFinding(Finding finding) => finding.Provenance == Provenance.Inferred
            ? FindingEnvelope.FromBaseRate(finding, ParallelCallAvailabilityUnevaluated)
            : FindingEnvelope.FromAdherence(finding, SampleFigure());

        var envelope = DigestEnvelope.From(digest, MapFinding);

        var adherence = Assert.Single(envelope.RankedFindings);
        Assert.IsType<FindingEnvelope.Adherence>(adherence);

        var baseRate = Assert.Single(envelope.InferredFindings);
        var typedBaseRate = Assert.IsType<FindingEnvelope.BaseRate>(baseRate);
        Assert.Equal(ParallelCallAvailabilityUnevaluated, typedBaseRate.UnevaluatedCondition);

        var json = JsonSerializer.Serialize(envelope);
        using var document = JsonDocument.Parse(json);
        var rankedKinds = document.RootElement.GetProperty("RankedFindings").EnumerateArray()
            .Select(item => item.GetProperty("kind").GetString())
            .ToList();
        var inferredKinds = document.RootElement.GetProperty("InferredFindings").EnumerateArray()
            .Select(item => item.GetProperty("kind").GetString())
            .ToList();

        Assert.Equal(["adherence"], rankedKinds);
        Assert.Equal(["baseRate"], inferredKinds);
    }
}
