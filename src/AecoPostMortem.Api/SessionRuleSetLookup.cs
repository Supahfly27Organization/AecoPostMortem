using AecoPostMortem.Data;
using AecoPostMortem.Data.Execution;
using AecoPostMortem.Ingestion;
using AecoPostMortem.Rules;

namespace AecoPostMortem.Api;

/// <summary>
/// FR-27's own not-yet-wired gap (`AecoPostMortem.Ingestion/CLAUDE.md`'s remarks under S-20):
/// <see cref="SessionRuleExtractor"/> only ever resolves one session's own <see cref="RawEvent"/>s,
/// already in hand — nothing walks a whole store calling it per session. This is that corpus-wide
/// wiring: one <see cref="SessionRuleSet"/> per <see cref="Session"/> row, carrying the repository and
/// start time <see cref="RuleSetVersioning"/>/<see cref="RulesInventory"/> both need to place it in
/// chronological order, the same narrow-RAW-read pattern <see cref="DeclaredIntentLookup"/> and
/// <see cref="HookFailureEventLookup"/> already use for their own not-yet-wired evidence.
/// </summary>
public static class SessionRuleSetLookup
{
    public static IReadOnlyList<SessionRuleSet> BuildAll(
        IReadOnlyList<Session> sessions, IReadOnlyList<RawEvent> rawEvents)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(rawEvents);

        var eventsBySession = rawEvents
            .GroupBy(e => e.SessionId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                IReadOnlyList<RawEvent> (group) => group.ToList(),
                StringComparer.Ordinal);

        return sessions
            .Select(session =>
            {
                var sessionEvents = eventsBySession.TryGetValue(session.SessionId, out var events)
                    ? events
                    : [];

                return new SessionRuleSet
                {
                    SessionId = session.SessionId,
                    Repository = session.Repository,
                    StartedAt = session.StartedAt,
                    Blocks = SessionRuleExtractor.Extract(session.SessionId, sessionEvents).Blocks,
                };
            })
            .ToList();
    }
}
