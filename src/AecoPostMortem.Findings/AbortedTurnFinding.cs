using System.Globalization;
using AecoPostMortem.Data.Execution;
using AecoPostMortem.Rules;

namespace AecoPostMortem.Findings;

/// <summary>
/// FR-18's orchestration (issue #28, S-16): reads <see cref="Turn"/> rows through
/// <c>AecoPostMortem.Data</c>, reduces each to <see cref="AecoPostMortem.Rules.TurnRecord"/>, calls
/// <see cref="AbortedTurnCheck"/> for every aborted turn's position in its own session, and writes
/// one <see cref="Finding"/> per aborted turn plus a <see cref="CheckRegistryEntry"/>.
/// </summary>
public static class AbortedTurnFinding
{
    /// <summary>The check's own identity in <see cref="CheckRegistry"/> — an abstract id, per
    /// <c>CheckRegistryEntry.CheckId</c>'s own remarks.</summary>
    public const string CheckId = "aborted-turn";

    public static (IReadOnlyList<Finding> Findings, CheckRegistryEntry Registry) Build(
        IReadOnlyList<Turn> turns)
    {
        ArgumentNullException.ThrowIfNull(turns);

        var population = turns.Select(turn => turn.SessionId).Distinct(StringComparer.Ordinal).Count();

        var records = turns
            .Select(turn => new TurnRecord
            {
                SessionId = turn.SessionId,
                EventId = turn.EventId,
                TurnId = turn.TurnId,
                StartedAt = turn.StartedAt,
                Aborted = turn.Outcome == TurnOutcome.Aborted,
                AbortReason = turn.AbortReason,
            })
            .ToArray();

        var occurrences = AbortedTurnCheck.Run(records);
        var findings = occurrences.Select(ToFinding).ToArray();

        var registryEntry = new CheckRegistryEntry
        {
            CheckId = CheckId,
            Status = CheckRunStatus.Ran,
            Population = population,
            FindingCount = findings.Length,
            Provenance = Provenance.Derived,
        };

        return (findings, registryEntry);
    }

    /// <summary>
    /// One finding per aborted turn, keyed by <c>(SessionId, EventId)</c> — the identical composite
    /// <c>PostMortemContext.MapTurn</c> keys <see cref="Turn"/> itself on, so a finding's identity
    /// <em>is</em> the identity of the entity that produced it rather than a plausible-looking
    /// near-miss. <c>EventId</c> is the <c>assistant.turn_start</c> envelope's own <c>id</c>; it is
    /// paired with <c>SessionId</c> rather than used alone for the same reason <c>Turn</c>'s own key
    /// is a composite.
    /// <para>This key was <c>(SessionId, TurnId)</c> until this change, which is the third instance
    /// of one root cause in this codebase — after <c>Turn</c>'s own primary key and
    /// <c>SessionTapeStep.StepId</c> for a Prompt step (PR #137). <c>data.turnId</c> is a small
    /// display counter Copilot reuses <em>within</em> one session
    /// (<c>AecoPostMortem.Data/CLAUDE.md</c>), so two aborts in the same session sharing a counter
    /// collided into one key — which <c>Recurrence.cs</c> documents as impossible ("no constructor
    /// that could produce a second `Finding` for the same key"). Measured against the live
    /// 35-session reference corpus the collision was latent, not live (9 aborted-turn findings, 9
    /// distinct keys both before and after; 6 of each in the dominant repository), because only one
    /// session has more than one aborted turn at all. The hazard it removes is not hypothetical
    /// though: 1,903 of 2,384 real turn rows, across 27 of 35 sessions, share their
    /// <c>(SessionId, TurnId)</c> pair with another turn — the key was drawn from a field that
    /// collides on 79.8% of the rows it is meant to identify, and only the rarity of aborts kept
    /// that from surfacing.</para>
    /// Unlike
    /// a hook name or a tool identity, an abort also has no recurring *cause* to group by: two
    /// aborts sharing the same reason text in different sessions are still two independent
    /// abandonments, so grouping on the reason would let a measured 9-across-8 volume read as
    /// fewer, more heavily recurring findings than it is (issue #28's edge case). The composite key
    /// is still stable across a re-ingest of the same session at a later rule-set version, so the
    /// same physical abort resolves to the same finding identity, per FR-57, without merging
    /// distinct events together.
    /// </summary>
    static Finding ToFinding(AbortedTurnOccurrence occurrence) => new()
    {
        Class = FindingClass.Waste,
        // Derived, not Observed: "position in the session" is computed by ordering every turn in
        // the session, not read from a single event's own field — the same reasoning
        // RepeatedFileReadFindingCheck gives for its own repeat count.
        Provenance = Provenance.Derived,
        Headline = BuildHeadline(occurrence),
        Evidence =
        [
            new EvidenceItem { Field = "data.reason", Value = occurrence.Reason },
            new EvidenceItem
            {
                Field = "position",
                Value = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} of {1}",
                    occurrence.Position,
                    occurrence.SessionTurnCount),
            },
        ],
        Recurrence = new Recurrence
        {
            Key = string.Concat(occurrence.SessionId, ":", occurrence.EventId),
            Occurrences = [new RecurrenceOccurrence { SessionId = occurrence.SessionId }],
        },
        Suggestion = BuildSuggestion(occurrence),
    };

    /// <summary>Mockup parity item #5: grounded in the same reason, position and session-turn-count
    /// data <see cref="Evidence"/> already carries for this turn.</summary>
    static string BuildHeadline(AbortedTurnOccurrence occurrence) => string.Format(
        CultureInfo.InvariantCulture,
        "A turn aborted (\"{0}\") at turn {1} of {2} in session {3}.",
        occurrence.Reason,
        occurrence.Position,
        occurrence.SessionTurnCount,
        occurrence.SessionId);

    /// <summary>
    /// Scenario 2 of issue #28: states the unknown outright rather than leaving it implicit —
    /// there is no rollback event in this event vocabulary, so whatever the aborted turn already
    /// wrote to disk before it stopped cannot be recovered from the log.
    /// </summary>
    static Suggestion BuildSuggestion(AbortedTurnOccurrence occurrence) => new()
    {
        Text = string.Format(
            CultureInfo.InvariantCulture,
            "This turn aborted (\"{0}\") at turn {1} of {2} in the session. No rollback event is "
                + "recorded, so what was already changed on disk before the abort is unknown.",
            occurrence.Reason,
            occurrence.Position,
            occurrence.SessionTurnCount),
    };
}
