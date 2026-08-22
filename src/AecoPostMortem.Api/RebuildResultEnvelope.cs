namespace AecoPostMortem.Api;

/// <summary>
/// POST /api/rebuild's response contract: how many RAW events and sessions the derived layer was
/// re-derived from, and how long it took (measured server-side, the same reasoning
/// <see cref="IngestResultEnvelope"/> documents for its own <c>DurationSeconds</c>). RAW itself is
/// never touched by a rebuild (<c>AecoPostMortem.Data/CLAUDE.md</c>'s "Only RAW carries a
/// migration"), so there is no coverage report here the way ingest has one — a rebuild reads only
/// what is already in the store.
/// </summary>
public sealed record RebuildResultEnvelope(long RawEventCount, int SessionCount, double DurationSeconds);
