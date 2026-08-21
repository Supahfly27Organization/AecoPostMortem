namespace AecoPostMortem.Findings;

/// <summary>
/// Mockup parity item #5 (FR-41's own headline gap, `docs/product-superpowers/discovery/
/// 2026-08-21-ui-mockup-parity.md`): the one formatting helper every check orchestrator's own
/// headline-building code shares — regular English pluralization only ("session"/"sessions",
/// "call"/"calls", "time"/"times") — the same narrow scope <c>SuggestionRenderer.FormatOperandList</c>
/// keeps for its own shared formatting concern rather than each orchestrator re-deriving the rule.
/// Every noun a headline pluralizes in this project takes a plain trailing "s" — there is no
/// irregular noun among the eleven headlines this helper serves, so a second overload for irregulars
/// is not built ahead of a check that would actually need one.
/// </summary>
static class HeadlineText
{
    public static string Pluralize(int count, string singular) => count == 1 ? singular : singular + "s";
}
