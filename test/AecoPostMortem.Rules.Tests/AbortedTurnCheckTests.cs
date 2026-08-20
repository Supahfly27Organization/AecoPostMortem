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
            new TurnRecord { SessionId = "s1", TurnId = "t1", StartedAt = "2026-08-16T10:00:00Z", Aborted = false },
            new TurnRecord { SessionId = "s1", TurnId = "t2", StartedAt = "2026-08-16T10:05:00Z", Aborted = false },
            new TurnRecord
            {
                SessionId = "s1",
                TurnId = "t3",
                StartedAt = "2026-08-16T10:10:00Z",
                Aborted = true,
                AbortReason = "user_interrupt",
            },
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
        var turns = new[]
        {
            new TurnRecord { SessionId = "s1", TurnId = "t1", StartedAt = "2026-08-16T10:00:00Z", Aborted = false },
        };

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
            new TurnRecord
            {
                SessionId = "s1",
                TurnId = "t1",
                StartedAt = "2026-08-16T10:00:00Z",
                Aborted = true,
                AbortReason = "user_interrupt",
            },
            new TurnRecord { SessionId = "s2", TurnId = "t1", StartedAt = "2026-08-16T09:00:00Z", Aborted = false },
            new TurnRecord { SessionId = "s2", TurnId = "t2", StartedAt = "2026-08-16T09:05:00Z", Aborted = false },
            new TurnRecord
            {
                SessionId = "s2",
                TurnId = "t3",
                StartedAt = "2026-08-16T09:10:00Z",
                Aborted = true,
                AbortReason = "timeout",
            },
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
            new TurnRecord
            {
                SessionId = "s1",
                TurnId = "t1",
                StartedAt = "2026-08-16T10:00:00Z",
                Aborted = true,
                AbortReason = "user_interrupt",
            },
            new TurnRecord { SessionId = "s1", TurnId = "t2", StartedAt = "2026-08-16T10:05:00Z", Aborted = false },
            new TurnRecord
            {
                SessionId = "s1",
                TurnId = "t3",
                StartedAt = "2026-08-16T10:10:00Z",
                Aborted = true,
                AbortReason = "timeout",
            },
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
    /// every run (PRD §3.8) — <c>TurnId</c>, compared ordinally, is the documented tiebreak.</summary>
    [Fact]
    public void Turns_sharing_the_same_started_at_break_the_tie_by_turn_id()
    {
        var turns = new[]
        {
            new TurnRecord
            {
                SessionId = "s1",
                TurnId = "b",
                StartedAt = "2026-08-16T10:00:00Z",
                Aborted = true,
                AbortReason = "timeout",
            },
            new TurnRecord { SessionId = "s1", TurnId = "a", StartedAt = "2026-08-16T10:00:00Z", Aborted = false },
        };

        var occurrences = AbortedTurnCheck.Run(turns);

        var occurrence = Assert.Single(occurrences);
        Assert.Equal("b", occurrence.TurnId);
        // "a" < "b" ordinally, so "a" (unaborted) takes position 1 and "b" (aborted) takes 2.
        Assert.Equal(2, occurrence.Position);
        Assert.Equal(2, occurrence.SessionTurnCount);
    }
}
