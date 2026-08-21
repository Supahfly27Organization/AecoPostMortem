namespace AecoPostMortem.Findings.Tests;

/// <summary>
/// Scenario 1 of issue #49 (S-39, FR-45): every response the operator gives a suggestion is stored
/// against the finding and its provenance level. The edge case: changing a verdict later must be
/// possible and must not lose the earlier one — <see cref="OperatorResponseLog"/> is append-only.
/// </summary>
public sealed class OperatorResponseLogTests
{
    private static OperatorResponseRecord Record(
        FindingClass findingClass,
        string recurrenceKey,
        Provenance provenance,
        OperatorResponse response,
        DateTimeOffset recordedAt) => new()
    {
        Class = findingClass,
        RecurrenceKey = recurrenceKey,
        Provenance = provenance,
        Response = response,
        RecordedAt = recordedAt,
    };

    [Fact]
    public void Recording_a_response_stores_it_against_the_finding_and_its_provenance_level()
    {
        var log = OperatorResponseLog.Empty.Record(Record(
            FindingClass.Waste,
            "src/foo.cs",
            Provenance.Derived,
            OperatorResponse.Accepted,
            DateTimeOffset.UnixEpoch));

        var entry = Assert.Single(log.Entries);
        Assert.Equal(FindingClass.Waste, entry.Class);
        Assert.Equal("src/foo.cs", entry.RecurrenceKey);
        Assert.Equal(Provenance.Derived, entry.Provenance);
        Assert.Equal(OperatorResponse.Accepted, entry.Response);
    }

    [Fact]
    public void Changing_a_verdict_later_does_not_lose_the_earlier_one()
    {
        var log = OperatorResponseLog.Empty
            .Record(Record(
                FindingClass.Waste,
                "src/foo.cs",
                Provenance.Derived,
                OperatorResponse.Accepted,
                DateTimeOffset.UnixEpoch))
            .Record(Record(
                FindingClass.Waste,
                "src/foo.cs",
                Provenance.Derived,
                OperatorResponse.Rejected,
                DateTimeOffset.UnixEpoch.AddDays(1)));

        Assert.Equal(2, log.Entries.Count);
        Assert.Equal(OperatorResponse.Accepted, log.Entries[0].Response);
        Assert.Equal(OperatorResponse.Rejected, log.Entries[1].Response);
    }

    [Fact]
    public void The_current_response_is_the_most_recently_recorded_one()
    {
        var log = OperatorResponseLog.Empty
            .Record(Record(
                FindingClass.Waste,
                "src/foo.cs",
                Provenance.Derived,
                OperatorResponse.Accepted,
                DateTimeOffset.UnixEpoch))
            .Record(Record(
                FindingClass.Waste,
                "src/foo.cs",
                Provenance.Derived,
                OperatorResponse.Rejected,
                DateTimeOffset.UnixEpoch.AddDays(1)));

        var current = Assert.Single(log.CurrentResponses());
        Assert.Equal(OperatorResponse.Rejected, current.Response);
    }

    /// <summary><see cref="OperatorResponseLog.CurrentResponses"/> claims that two entries sharing
    /// one <see cref="OperatorResponseRecord.RecordedAt"/> instant still resolve deterministically —
    /// to whichever was appended later — because the reduction's <c>OrderBy</c> is stable.</summary>
    [Fact]
    public void A_tied_timestamp_resolves_to_whichever_entry_was_appended_later()
    {
        var tie = DateTimeOffset.UnixEpoch;
        var log = OperatorResponseLog.Empty
            .Record(Record(FindingClass.Waste, "src/foo.cs", Provenance.Derived, OperatorResponse.Accepted, tie))
            .Record(Record(FindingClass.Waste, "src/foo.cs", Provenance.Derived, OperatorResponse.Rejected, tie));

        var current = Assert.Single(log.CurrentResponses());
        Assert.Equal(OperatorResponse.Rejected, current.Response);
    }

    [Fact]
    public void Two_different_findings_each_carry_their_own_current_response()
    {
        var log = OperatorResponseLog.Empty
            .Record(Record(
                FindingClass.Waste,
                "src/foo.cs",
                Provenance.Derived,
                OperatorResponse.Accepted,
                DateTimeOffset.UnixEpoch))
            .Record(Record(
                FindingClass.MissingCapability,
                "web_fetch",
                Provenance.Inferred,
                OperatorResponse.Rejected,
                DateTimeOffset.UnixEpoch));

        Assert.Equal(2, log.CurrentResponses().Count);
    }

    [Fact]
    public void Applying_the_log_to_a_finding_populates_its_operator_response_field()
    {
        var finding = new Finding
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

        var log = OperatorResponseLog.Empty.Record(Record(
            FindingClass.Waste,
            "src/foo.cs",
            Provenance.Derived,
            OperatorResponse.Rejected,
            DateTimeOffset.UnixEpoch));

        var applied = log.Apply(finding);

        Assert.Equal(OperatorResponse.Rejected, applied.OperatorResponse);
    }

    [Fact]
    public void A_finding_with_no_recorded_response_stays_ignored_when_applied()
    {
        var finding = new Finding
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

        var applied = OperatorResponseLog.Empty.Apply(finding);

        Assert.Equal(OperatorResponse.Ignored, applied.OperatorResponse);
    }
}
