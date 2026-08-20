namespace AecoPostMortem.Findings.Tests;

/// <summary>
/// FR-21 part 2 of 3 (S-52, issue #16): the finding chip row — a different data path from
/// <see cref="SessionRecording"/>. A finding "affects" a session when <see cref="Recurrence"/>
/// carries a <see cref="RecurrenceOccurrence"/> naming that session (FR-57), and each chip carries
/// <see cref="ProcessDigest.SessionsAffected"/> — the same corpus-wide figure the digest already
/// ranks by, reused rather than a second, check-specific "count" concept invented for this surface.
/// </summary>
public sealed class SessionFindingsTests
{
    static Finding FindingTouching(string key, params string[] sessionIds) => new()
    {
        Class = FindingClass.Waste,
        Provenance = Provenance.Derived,
        Evidence = [],
        Recurrence = new Recurrence
        {
            Key = key,
            Occurrences = sessionIds.Select(id => new RecurrenceOccurrence { SessionId = id }).ToList(),
        },
    };

    [Fact]
    public void A_finding_whose_recurrence_names_the_session_becomes_a_chip()
    {
        var finding = FindingTouching("hook:pre-commit", "s1");

        var result = SessionFindings.For("s1", [finding]);

        var chip = Assert.Single(result.Chips);
        Assert.Same(finding, chip.Finding);
    }

    [Fact]
    public void A_finding_that_never_names_this_session_produces_no_chip()
    {
        var finding = FindingTouching("hook:pre-commit", "s2");

        var result = SessionFindings.For("s1", [finding]);

        Assert.Empty(result.Chips);
    }

    [Fact]
    public void A_chip_carries_the_finding_s_corpus_wide_sessions_affected_count()
    {
        var finding = FindingTouching("path:/repo/a.cs", "s1", "s2", "s3");

        var result = SessionFindings.For("s1", [finding]);

        var chip = Assert.Single(result.Chips);
        Assert.Equal(3, chip.SessionsAffected);
    }

    [Fact]
    public void A_session_carrying_no_findings_produces_an_empty_chip_row_not_a_null_one()
    {
        var result = SessionFindings.For("s1", []);

        Assert.NotNull(result.Chips);
        Assert.Empty(result.Chips);
    }

    [Fact]
    public void For_rejects_a_null_findings_list()
    {
        Assert.Throws<ArgumentNullException>(() => SessionFindings.For("s1", null!));
    }

    [Fact]
    public void For_rejects_a_blank_session_id()
    {
        Assert.Throws<ArgumentException>(() => SessionFindings.For("", []));
    }
}
