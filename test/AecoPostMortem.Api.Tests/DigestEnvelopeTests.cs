using System.Text.Json;
using AecoPostMortem.Findings;

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

    [Fact]
    public void From_carries_the_digest_state_and_maps_every_ranked_finding_in_order()
    {
        var digest = ProcessDigest.Build(
            Counters(),
            RanCleanRegistry(),
            [WasteFinding("src/rare.cs", "session-1"), WasteFinding("src/hot.cs", "session-1", "session-2")]);

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
        var digest = ProcessDigest.Build(Counters(), RanCleanRegistry(), []);

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
    public void DigestEnvelope_serialises_the_state_and_the_ranked_findings()
    {
        var digest = ProcessDigest.Build(Counters(), RanCleanRegistry(), [WasteFinding("src/hot.cs", "session-1")]);
        var envelope = DigestEnvelope.From(digest, FindingEnvelope.From);

        var json = JsonSerializer.Serialize(envelope);
        using var document = JsonDocument.Parse(json);

        Assert.Equal("Analyzed", document.RootElement.GetProperty("State").GetString());
        Assert.Equal(1, document.RootElement.GetProperty("RankedFindings").GetArrayLength());

        var masthead = document.RootElement.GetProperty("Masthead");
        Assert.True(masthead.TryGetProperty("SessionCount", out _));
        Assert.Equal("NotYetAnalyzed", masthead.GetProperty("RuleCoverage").GetString());
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
            []);

        var envelope = DigestEnvelope.From(digest, FindingEnvelope.From);

        Assert.Equal(DigestState.NotYetAnalyzed, envelope.State);
        Assert.Empty(envelope.RankedFindings);
    }

    // FR-44, Scenario 2 ("A base rate is never ranked as a violation"): the conditional-rule
    // finding touches more sessions than the measured adherence finding, so it ranks first — and
    // still has to render with a wire shape a client can never mistake for the measured violation
    // beside it.
    [Fact]
    public void A_base_rate_item_ranked_above_a_measured_violation_still_serialises_a_distinct_kind()
    {
        var digest = ProcessDigest.Build(
            Counters(),
            RanCleanRegistry(),
            [
                AdherenceFinding("prefer rg over grep", "session-1"),
                ConditionalRuleFinding("session-1", "session-2", "session-3"),
            ]);

        FindingEnvelope MapFinding(Finding finding) => finding.Provenance == Provenance.Inferred
            ? FindingEnvelope.FromBaseRate(finding, ParallelCallAvailabilityUnevaluated)
            : FindingEnvelope.FromAdherence(finding, finding.Resolution!, ruleVersion: "v3");

        var envelope = DigestEnvelope.From(digest, MapFinding);

        Assert.Equal(2, envelope.RankedFindings.Count);
        var baseRate = Assert.IsType<FindingEnvelope.BaseRate>(envelope.RankedFindings[0]);
        var adherence = Assert.IsType<FindingEnvelope.Adherence>(envelope.RankedFindings[1]);
        Assert.Equal(ParallelCallAvailabilityUnevaluated, baseRate.UnevaluatedCondition);

        var json = JsonSerializer.Serialize(envelope);
        using var document = JsonDocument.Parse(json);
        var kinds = document.RootElement.GetProperty("RankedFindings").EnumerateArray()
            .Select(item => item.GetProperty("kind").GetString())
            .ToList();

        Assert.Equal(["baseRate", "adherence"], kinds);
    }
}
