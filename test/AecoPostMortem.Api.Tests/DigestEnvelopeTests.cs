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
}
