namespace AecoPostMortem.Api;

/// <summary>
/// GET /api/settings's response contract: the operator's real, currently-resolved configuration —
/// nothing here is guessed or derived beyond what <see cref="AecoPostMortem.Data.LocalStore"/> and
/// <see cref="AecoPostMortem.Ingestion.ExclusionListSource"/> already resolve for <c>ingest</c>
/// itself, so this surface can never disagree with what a real <c>ingest</c> run would actually do.
/// <see cref="StoreExists"/> is <see langword="false"/> before the first ingest — a real, honest
/// state the client renders distinctly (`web/CLAUDE.md`'s "an honest empty state, never a guessed
/// number" discipline), not a bare <c>0</c> that would read as a measured, empty-but-real store.
/// </summary>
/// <remarks>
/// <see cref="StoreIsAtDefaultLocation"/> answers the question an operator actually has about a
/// path they may not recognise — "is this where the store is supposed to be?" — rather than
/// reporting *how* the path was chosen. That distinction is deliberate: the mechanism
/// (<c>--store &lt;path&gt;</c>, <c>Cli/CLAUDE.md</c>) is not visible to this project at all, and
/// plumbing it through would add a second fact that could disagree with the path actually open.
/// Comparing the resolved path against <see cref="AecoPostMortem.Data.StoreLocation.Default"/> is a
/// fact about the store in hand, not a guess about intent — and it stays correct for
/// <c>--store &lt;the default path&gt;</c>, which genuinely *is* the documented location.
/// </remarks>
public sealed record SettingsEnvelope(
    string StorePath,
    bool StoreExists,
    long StoreSizeBytes,
    bool StoreIsAtDefaultLocation,
    string CopilotSourceRoot,
    bool CopilotSourceFound,
    IReadOnlyList<string> ExcludedRoots);
