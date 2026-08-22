using AecoPostMortem.Data.Execution;

namespace AecoPostMortem.Findings.Tests;

/// <summary>
/// FR-18 (issue #28, S-16): orchestration over <c>AecoPostMortem.Rules.AbortedTurnCheck</c> —
/// turning <c>Turn</c> rows into one <c>Finding</c> per aborted turn, each carrying its reason and
/// its position in the session, plus the stated unknown that no rollback event exists. Measured 9
/// aborts across 8 sessions is low volume (the issue's edge case): findings are never grouped
/// across turns, so that measured count cannot be inflated by collapsing distinct aborts together.
/// </summary>
public sealed class AbortedTurnFindingTests
{
    [Fact]
    public void An_abort_is_reported_with_its_reason_and_its_position_in_the_session()
    {
        var turns = new[]
        {
            BuildTurn("s1", "t1", "2026-08-16T10:00:00Z", TurnOutcome.Completed),
            BuildTurn("s1", "t2", "2026-08-16T10:05:00Z", TurnOutcome.Completed),
            BuildTurn("s1", "t3", "2026-08-16T10:10:00Z", TurnOutcome.Aborted, "user_interrupt"),
        };

        var (findings, registry) = AbortedTurnFinding.Build(turns);

        var finding = Assert.Single(findings);
        Assert.Equal(FindingClass.Waste, finding.Class);
        Assert.Contains(finding.Evidence, item => item.Field == "data.reason" && item.Value == "user_interrupt");
        Assert.Contains(finding.Evidence, item => item.Field == "position" && item.Value == "3 of 3");
        Assert.Equal("A turn aborted (\"user_interrupt\") at turn 3 of 3 in session s1.", finding.Headline);
        Assert.Equal(1, registry.FindingCount);
        Assert.Equal(1, registry.Population);
    }

    /// <summary>Scenario 2 of issue #28: the unknown is stated, not left implicit.</summary>
    [Fact]
    public void The_suggestion_states_that_no_rollback_event_is_recorded()
    {
        var turns = new[] { BuildTurn("s1", "t1", "2026-08-16T10:00:00Z", TurnOutcome.Aborted, "timeout") };

        var (findings, _) = AbortedTurnFinding.Build(turns);

        var finding = Assert.Single(findings);
        Assert.Contains("no rollback event", finding.Suggestion!.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("unknown", finding.Suggestion!.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_completed_turn_produces_no_finding()
    {
        var turns = new[] { BuildTurn("s1", "t1", "2026-08-16T10:00:00Z", TurnOutcome.Completed) };

        var (findings, registry) = AbortedTurnFinding.Build(turns);

        Assert.Empty(findings);
        Assert.Equal(CheckRunStatus.Ran, registry.Status);
        Assert.Equal(0, registry.FindingCount);
        Assert.Equal(1, registry.Population);
        Assert.Equal(Provenance.Derived, registry.Provenance);
    }

    /// <summary>The edge case named in issue #28: two aborts sharing the same reason text in
    /// different sessions must not collapse into one finding that reads as more recurring than the
    /// measured volume.</summary>
    [Fact]
    public void Distinct_aborted_turns_produce_distinct_findings_even_when_the_reason_matches()
    {
        var turns = new[]
        {
            BuildTurn("s1", "t1", "2026-08-16T10:00:00Z", TurnOutcome.Aborted, "user_interrupt"),
            BuildTurn("s2", "t1", "2026-08-16T09:00:00Z", TurnOutcome.Aborted, "user_interrupt"),
        };

        var (findings, registry) = AbortedTurnFinding.Build(turns);

        Assert.Equal(2, findings.Count);
        Assert.Equal(2, registry.FindingCount);
        Assert.All(findings, finding => Assert.Single(finding.Recurrence.Occurrences));
    }

    /// <summary>
    /// A `Turn`'s own natural key is `(SessionId, TurnId)` (`PostMortemContext.MapTurn`) —
    /// `TurnId` alone is not guaranteed unique across sessions. Two aborted turns in different
    /// sessions that happen to share a bare `TurnId` must still resolve to two distinct
    /// `Recurrence.Key`s, or they collide into what `Recurrence.cs` documents as impossible: "no
    /// constructor that could produce a second `Finding` for the same key."
    /// </summary>
    [Fact]
    public void The_recurrence_key_does_not_collide_across_sessions_sharing_a_bare_turn_id()
    {
        var turns = new[]
        {
            BuildTurn("s1", "t1", "2026-08-16T10:00:00Z", TurnOutcome.Aborted, "user_interrupt"),
            BuildTurn("s2", "t1", "2026-08-16T09:00:00Z", TurnOutcome.Aborted, "timeout"),
        };

        var (findings, _) = AbortedTurnFinding.Build(turns);

        var keys = findings.Select(finding => finding.Recurrence.Key).ToArray();
        Assert.Equal(2, keys.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// The defect this test exists for: <c>data.turnId</c> is a small display counter Copilot
    /// reuses <em>within</em> one session — measured against the live 35-session reference corpus,
    /// 1,903 of 2,384 real turn rows share their <c>(SessionId, TurnId)</c> pair with another turn,
    /// across 27 of 35 sessions. A key built from it therefore merges two genuinely distinct aborts
    /// in the same session into one <c>Recurrence.Key</c>, which <c>Recurrence.cs</c> documents as
    /// impossible ("no constructor that could produce a second `Finding` for the same key"). The key
    /// must come from <see cref="Turn.EventId"/> — the turn's own key, the same field
    /// <c>PostMortemContext.MapTurn</c> keys the entity on.
    /// </summary>
    [Fact]
    public void The_recurrence_key_does_not_collide_within_one_session_sharing_a_display_counter()
    {
        var turns = new[]
        {
            BuildTurn("s1", "3", "2026-08-16T10:00:00Z", TurnOutcome.Aborted, "user_interrupt", eventId: "e-first"),
            BuildTurn("s1", "3", "2026-08-16T10:10:00Z", TurnOutcome.Aborted, "timeout", eventId: "e-second"),
        };

        var (findings, _) = AbortedTurnFinding.Build(turns);

        Assert.Equal(2, findings.Count);
        var keys = findings.Select(finding => finding.Recurrence.Key).ToArray();
        Assert.Equal(2, keys.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>The key is the turn's own <c>(SessionId, EventId)</c> pair — the identical composite
    /// <c>PostMortemContext.MapTurn</c> keys <c>Turn</c> on — never its display counter. Pinned as an
    /// exact value, not merely as "distinct", so a future change cannot satisfy the collision test
    /// above by inventing some third identifier that is unique but is not the entity's own key.</summary>
    [Fact]
    public void The_recurrence_key_is_the_turns_own_session_and_event_id()
    {
        var turns = new[]
        {
            BuildTurn("s1", "3", "2026-08-16T10:00:00Z", TurnOutcome.Aborted, "timeout", eventId: "e-first"),
        };

        var (findings, _) = AbortedTurnFinding.Build(turns);

        var finding = Assert.Single(findings);
        Assert.Equal("s1:e-first", finding.Recurrence.Key);
    }

    [Fact]
    public void Every_finding_carries_derived_provenance()
    {
        var turns = new[] { BuildTurn("s1", "t1", "2026-08-16T10:00:00Z", TurnOutcome.Aborted, "timeout") };

        var (findings, _) = AbortedTurnFinding.Build(turns);

        Assert.All(findings, finding => Assert.Equal(Provenance.Derived, finding.Provenance));
    }

    static Turn BuildTurn(
        string sessionId,
        string turnId,
        string startedAt,
        TurnOutcome outcome,
        string? abortReason = null,
        string? eventId = null) => new()
    {
        SessionId = sessionId,
        // Defaulted from TurnId only for the cases that do not care which is which; a test about
        // identity must state it explicitly, since the whole point is that the two can differ.
        EventId = eventId ?? $"e-{turnId}",
        TurnId = turnId,
        StartedAt = startedAt,
        Outcome = outcome,
        AbortReason = abortReason,
        OwnerKind = OwnerKind.Main,
    };
}
