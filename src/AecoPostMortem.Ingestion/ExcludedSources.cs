namespace AecoPostMortem.Ingestion;

/// <summary>
/// FR-10: the one Copilot source this product refuses to open. Copilot's global
/// <c>session-store.db</c> — not the per-session <c>session.db</c> FR-1 classifies but leaves
/// unread for a different reason — is live-written and WAL-dependent, covers a measured 7 of 40
/// sessions, and everything it uniquely offers is per-request latency and nano-AIU: nothing any
/// finding class needs. Recorded here as a decision, not rediscovered as an oversight.
/// </summary>
public static class ExcludedSources
{
    /// <summary>The exact file name of Copilot's global session-store database, sitting directly
    /// under the Copilot root rather than under a session directory.</summary>
    public const string SessionStoreDatabaseFileName = "session-store.db";

    /// <summary>What a coverage report states about this source (FR-14 consumes this; FR-10 is why
    /// it is true). Kept as a stated value rather than left for each caller to phrase, so "skipped
    /// by design" is one sentence, not several that could drift.</summary>
    public const string SkipReason =
        "Copilot's session-store database is excluded by design: it is live-written and "
        + "WAL-dependent, covers a measured 7 of 40 sessions, and offers nothing any finding class "
        + "needs (FR-10).";

    /// <summary>True for the global session-store database, by file name — position doesn't
    /// disambiguate it from anything else this product reads, since nothing else shares the name.</summary>
    public static bool IsExcluded(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        return string.Equals(
            Path.GetFileName(path),
            SessionStoreDatabaseFileName,
            StringComparison.Ordinal);
    }
}
