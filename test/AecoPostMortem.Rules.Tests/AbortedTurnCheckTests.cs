namespace AecoPostMortem.Rules.Tests;

/// <summary>
/// FR-18's check (issue #28, S-16): every aborted turn, placed among the turns of its own
/// session. Measured 9 aborts across 8 sessions is low volume, so the check reports one occurrence
/// per aborted turn rather than aggregating — nothing here groups two aborts together.
/// </summary>
public sealed class AbortedTurnCheckTests
{
    [Fact]
    public void An_aborted_turn_reports_its_reason_and_its_one_based_position_in_the_session()
    {
        var turns = new[]
        {
            Turn("s1", "t1", "2026-08-16T10:00:00Z", aborted: false),
            Turn("s1", "t2", "2026-08-16T10:05:00Z", aborted: false),
            Turn("s1", "t3", "2026-08-16T10:10:00Z", aborted: true, reason: "user_interrupt"),
        };

        var occurrences = AbortedTurnCheck.Run(turns);

        var occurrence = Assert.Single(occurrences);
        Assert.Equal("s1", occurrence.SessionId);
        Assert.Equal("t3", occurrence.TurnId);
        Assert.Equal("user_interrupt", occurrence.Reason);
        Assert.Equal(3, occurrence.Position);
        Assert.Equal(3, occurrence.SessionTurnCount);
    }

    [Fact]
    public void A_completed_or_unfinished_turn_produces_no_occurrence()
    {
        var turns = new[] { Turn("s1", "t1", "2026-08-16T10:00:00Z", aborted: false) };

        var occurrences = AbortedTurnCheck.Run(turns);

        Assert.Empty(occurrences);
    }

    /// <summary>Position is per-session: a turn aborting early in a long session and a turn
    /// aborting late in a short one are not conflated just because both happen to be "turn 1".</summary>
    [Fact]
    public void Position_is_scoped_to_the_turns_own_session_not_the_whole_corpus()
    {
        var turns = new[]
        {
            Turn("s1", "t1", "2026-08-16T10:00:00Z", aborted: true, reason: "user_interrupt"),
            Turn("s2", "t1", "2026-08-16T09:00:00Z", aborted: false),
            Turn("s2", "t2", "2026-08-16T09:05:00Z", aborted: false),
            Turn("s2", "t3", "2026-08-16T09:10:00Z", aborted: true, reason: "timeout"),
        };

        var occurrences = AbortedTurnCheck.Run(turns);

        Assert.Equal(2, occurrences.Count);

        var s1Occurrence = Assert.Single(occurrences, o => o.SessionId == "s1");
        Assert.Equal(1, s1Occurrence.Position);
        Assert.Equal(1, s1Occurrence.SessionTurnCount);

        var s2Occurrence = Assert.Single(occurrences, o => o.SessionId == "s2");
        Assert.Equal(3, s2Occurrence.Position);
        Assert.Equal(3, s2Occurrence.SessionTurnCount);
    }

    [Fact]
    public void Several_aborts_in_one_session_each_report_their_own_position()
    {
        var turns = new[]
        {
            Turn("s1", "t1", "2026-08-16T10:00:00Z", aborted: true, reason: "user_interrupt"),
            Turn("s1", "t2", "2026-08-16T10:05:00Z", aborted: false),
            Turn("s1", "t3", "2026-08-16T10:10:00Z", aborted: true, reason: "timeout"),
        };

        var occurrences = AbortedTurnCheck.Run(turns);

        Assert.Equal(2, occurrences.Count);
        Assert.Contains(occurrences, o => o.TurnId == "t1" && o.Position == 1 && o.SessionTurnCount == 3);
        Assert.Contains(occurrences, o => o.TurnId == "t3" && o.Position == 3 && o.SessionTurnCount == 3);
    }

    [Fact]
    public void An_empty_corpus_yields_no_occurrences()
    {
        Assert.Empty(AbortedTurnCheck.Run([]));
    }

    /// <summary>Two turns sharing one <c>StartedAt</c> value must still order the same way on
    /// every run (PRD §3.8) — <c>EventId</c>, compared ordinally, is the tiebreak. It replaced
    /// <c>TurnId</c> because a display counter is exactly the field two turns of one session are
    /// most likely to share: tie-breaking on it leaves a genuine tie unbroken, which is the
    /// non-determinism this tiebreak exists to rule out. Both turns here share a <c>TurnId</c>, so
    /// this fixture is unorderable under the old tiebreak and deterministic under the new one.</summary>
    [Fact]
    public void Turns_sharing_the_same_started_at_break_the_tie_by_event_id()
    {
        var turns = new[]
        {
            new TurnRecord
            {
                SessionId = "s1",
                EventId = "b",
                TurnId = "1",
                StartedAt = "2026-08-16T10:00:00Z",
                Aborted = true,
                AbortReason = "timeout",
            },
            new TurnRecord
            {
                SessionId = "s1",
                EventId = "a",
                TurnId = "1",
                StartedAt = "2026-08-16T10:00:00Z",
                Aborted = false,
            },
        };

        var occurrences = AbortedTurnCheck.Run(turns);

        var occurrence = Assert.Single(occurrences);
        Assert.Equal("b", occurrence.EventId);
        // "a" < "b" ordinally, so "a" (unaborted) takes position 1 and "b" (aborted) takes 2.
        Assert.Equal(2, occurrence.Position);
        Assert.Equal(2, occurrence.SessionTurnCount);
    }

    /// <summary>
    /// The identity defect this shape was widened for: a turn's display counter repeats within one
    /// session (measured against the live reference corpus, 1,903 of 2,384 real turn rows share
    /// their <c>(SessionId, TurnId)</c> pair with another turn, across 27 of 35 sessions), so an
    /// occurrence has to carry the id of the event that opened its turn to be addressable at all.
    /// Two aborts in one session under one counter are two occurrences, and stay distinguishable.
    /// </summary>
    [Fact]
    public void Two_aborts_in_one_session_sharing_a_display_counter_stay_distinguishable()
    {
        var turns = new[]
        {
            new TurnRecord
            {
                SessionId = "s1",
                EventId = "e-first",
                TurnId = "3",
                StartedAt = "2026-08-16T10:00:00Z",
                Aborted = true,
                AbortReason = "user_interrupt",
            },
            new TurnRecord
            {
                SessionId = "s1",
                EventId = "e-second",
                TurnId = "3",
                StartedAt = "2026-08-16T10:10:00Z",
                Aborted = true,
                AbortReason = "timeout",
            },
        };

        var occurrences = AbortedTurnCheck.Run(turns);

        Assert.Equal(2, occurrences.Count);
        Assert.Equal(2, occurrences.Select(o => o.EventId).Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(occurrences, o => o.EventId == "e-first" && o.Position == 1);
        Assert.Contains(occurrences, o => o.EventId == "e-second" && o.Position == 2);
    }

    /// <summary>An occurrence carries the identity of the turn it came from, not merely its
    /// display counter — the field a caller builds a per-abort key out of.</summary>
    [Fact]
    public void An_occurrence_carries_the_event_id_of_the_turn_it_came_from()
    {
        var turns = new[] { Turn("s1", "t1", "2026-08-16T10:00:00Z", aborted: true, reason: "timeout") };

        var occurrence = Assert.Single(AbortedTurnCheck.Run(turns));

        Assert.Equal("e-t1", occurrence.EventId);
    }

    /// <summary>The cases above that do not turn on identity take an event id derived from the
    /// display counter; a test that <em>is</em> about identity states both explicitly, since the
    /// whole point is that the two can differ.</summary>
    static TurnRecord Turn(
        string sessionId,
        string turnId,
        string startedAt,
        bool aborted,
        string? reason = null) => new()
    {
        SessionId = sessionId,
        EventId = $"e-{turnId}",
        TurnId = turnId,
        StartedAt = startedAt,
        Aborted = aborted,
        AbortReason = reason,
    };
}
