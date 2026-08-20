using AecoPostMortem.Data.Execution;

namespace AecoPostMortem.Findings;

/// <summary>
/// FR-24 (S-11, issue #20): the masthead's session-scoped token totals — read verbatim from
/// <see cref="Session"/>'s own token fields (<c>session.shutdown.data.modelMetrics</c>), never
/// computed. Closed to exactly two shapes through the private constructor, the same closed-union
/// reasoning <c>SuggestionEnvelope</c> uses for its own absent state (<c>AecoPostMortem.Api</c>):
/// "no totals recorded" must be an explicit state, never a zero the product invented for the
/// measured 4 of 35 sessions whose shutdown event carried no metrics.
///
/// No shape in this file — and nothing <see cref="From"/> can produce — carries a cost, price or
/// currency field. Copilot prices in premium requests and nano-AIU, and no local file states a
/// conversion rate: apportioning a total into a price is Inferred, and FR-24 says this product does
/// not do it, anywhere.
/// </summary>
public abstract record SessionTokenFigures
{
    private SessionTokenFigures()
    {
    }

    /// <summary>The one value representing "this session's shutdown event carried no token
    /// metrics" (Scenario 2) — never a zero-filled <see cref="Observed"/>.</summary>
    public static SessionTokenFigures NotRecorded { get; } = new SessionTotalsNotRecorded();

    /// <summary>
    /// Reads a session's own token fields into the masthead's two explicit states (Scenario 1).
    /// <see cref="Session.InputTokens"/> and <see cref="Session.OutputTokens"/> are the totals FR-24
    /// names and are required together for <see cref="Observed"/>, because both come from the same
    /// shutdown event: a session carrying only one of the pair is not a partial total, it is a
    /// missing one, so it reports <see cref="NotRecorded"/> the same as a session carrying neither.
    /// </summary>
    public static SessionTokenFigures From(Session session)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (session.InputTokens is not long inputTokens || session.OutputTokens is not long outputTokens)
        {
            return NotRecorded;
        }

        return new Observed
        {
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            CacheReadTokens = session.CacheReadTokens,
            CacheWriteTokens = session.CacheWriteTokens,
            ReasoningTokens = session.ReasoningTokens,
            ModelCount = session.ModelCount,
        };
    }

    /// <summary>Token totals read from <c>session.shutdown.data.modelMetrics</c>, marked Observed.
    /// The three supplementary fields stay nullable, exactly as <see cref="Session"/> stores them:
    /// not every model reports cache or reasoning activity, and that absence is not the same fact as
    /// the shutdown event never firing at all.</summary>
    public sealed record Observed : SessionTokenFigures
    {
        public required long InputTokens { get; init; }

        public required long OutputTokens { get; init; }

        public long? CacheReadTokens { get; init; }

        public long? CacheWriteTokens { get; init; }

        public long? ReasoningTokens { get; init; }

        public int? ModelCount { get; init; }
    }

    /// <summary>Scenario 2: a session with no shutdown metrics states that plainly rather than
    /// showing zero.</summary>
    public sealed record SessionTotalsNotRecorded : SessionTokenFigures;
}
