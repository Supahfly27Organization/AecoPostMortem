namespace AecoPostMortem.Ingestion.Tests;

/// <summary>
/// FR-7: whether a session's own <c>cwd</c> falls under an operator-configured exclusion root.
/// Pure and file-free — <see cref="ExclusionListSource"/> is what reads the operator's own
/// configured list.
/// </summary>
public sealed class SessionExclusionTests
{
    [Fact]
    public void A_cwd_under_an_excluded_root_is_excluded()
    {
        var outcome = SessionExclusion.Evaluate(
            @"C:\repo\AecoPostMortem\src\AecoPostMortem.Ingestion",
            [@"C:\repo\AecoPostMortem"]);

        Assert.True(outcome.Excluded);
    }

    [Fact]
    public void A_cwd_equal_to_the_excluded_root_is_excluded()
    {
        var outcome = SessionExclusion.Evaluate(@"C:\repo\AecoPostMortem", [@"C:\repo\AecoPostMortem"]);

        Assert.True(outcome.Excluded);
    }

    /// <summary>A sibling directory sharing a name prefix must never match — <c>/repo</c> is not a
    /// prefix of <c>/repository</c> in the directory sense, even though it is one as raw text.</summary>
    [Fact]
    public void A_sibling_directory_sharing_a_name_prefix_is_not_excluded()
    {
        var outcome = SessionExclusion.Evaluate(@"C:\repo-other\file.txt", [@"C:\repo"]);

        Assert.False(outcome.Excluded);
    }

    [Fact]
    public void A_cwd_outside_every_root_is_not_excluded()
    {
        var outcome = SessionExclusion.Evaluate(@"C:\work\feature-x", [@"C:\repo\AecoPostMortem"]);

        Assert.False(outcome.Excluded);
    }

    /// <summary>This product cannot exclude a session it cannot place — an unknown cwd is never
    /// excluded, even against a non-empty list.</summary>
    [Fact]
    public void An_unknown_cwd_is_never_excluded()
    {
        var outcome = SessionExclusion.Evaluate(null, [@"C:\repo\AecoPostMortem"]);

        Assert.False(outcome.Excluded);
    }

    [Fact]
    public void Backslash_and_forward_slash_paths_are_compared_the_same_way()
    {
        var outcome = SessionExclusion.Evaluate(
            "C:/repo/AecoPostMortem/src",
            [@"C:\repo\AecoPostMortem"]);

        Assert.True(outcome.Excluded);
    }

    [Fact]
    public void The_reason_names_the_cwd_and_the_matching_root()
    {
        var outcome = SessionExclusion.Evaluate(
            @"C:\repo\AecoPostMortem\src",
            [@"C:\repo\AecoPostMortem"]);

        Assert.Contains(@"C:\repo\AecoPostMortem\src", outcome.Reason);
        Assert.Contains(@"C:\repo\AecoPostMortem", outcome.Reason);
    }

    [Fact]
    public void A_not_excluded_outcome_carries_no_reason()
    {
        var outcome = SessionExclusion.Evaluate(@"C:\work\feature-x", []);

        Assert.Null(outcome.Reason);
    }
}
