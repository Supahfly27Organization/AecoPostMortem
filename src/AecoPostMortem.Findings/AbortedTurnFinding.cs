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
    /// One finding per aborted turn, keyed by <c>(SessionId, TurnId)</c> — <see cref="Turn"/>'s own
    /// natural key (<c>PostMortemContext.MapTurn</c>), not <see cref="AbortedTurnOccurrence.TurnId"/>
    /// alone, because a bare `TurnId` is not guaranteed unique across sessions and colliding two
    /// unrelated aborts into one key would violate what <c>Recurrence.cs</c> documents as
    /// impossible: "no constructor that could produce a second `Finding` for the same key." Unlike
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
            Key = string.Concat(occurrence.SessionId, ":", occurrence.TurnId),
            Occurrences = [new RecurrenceOccurrence { SessionId = occurrence.SessionId }],
        },
        Suggestion = BuildSuggestion(occurrence),
    };

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
