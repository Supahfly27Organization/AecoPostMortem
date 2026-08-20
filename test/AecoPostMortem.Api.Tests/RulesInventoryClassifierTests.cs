using AecoPostMortem.Rules;

namespace AecoPostMortem.Api.Tests;

/// <summary>
/// FR-40's classify function, consumed for real for the first time: every statement the catalogue
/// matched to a shape is <see cref="RuleStatementStatus.CheckableNotYetBuilt"/> — no built check
/// watches any shape against the real corpus yet (the same gap <c>ToolVocabularyMismatchCheck</c> not
/// being wired into <c>ApiHost.GetDigest</c> documents) — and FR-34's own two unmatched dispositions
/// map onto FR-40's remaining two statuses this piece can answer honestly:
/// <see cref="UnmatchedStatementDisposition.CheckableNotBuilt"/> is also
/// <see cref="RuleStatementStatus.CheckableNotYetBuilt"/>, and
/// <see cref="UnmatchedStatementDisposition.NotCheckable"/> is <see cref="RuleStatementStatus.NotARule"/>.
/// </summary>
public sealed class RulesInventoryClassifierTests
{
    static RuleStatement Statement(string text) => new() { SourceFile = "CLAUDE.md", Text = text };

    [Fact]
    public void A_statement_the_catalogue_matched_to_a_shape_is_checkable_not_yet_built()
    {
        var statement = Statement("Never read secrets.env.");
        var matching = RuleShapeCatalogue.MatchAll([statement]);

        var classify = RulesInventoryClassifier.BuildClassifier(matching);

        Assert.Equal(RuleStatementStatus.CheckableNotYetBuilt, classify(statement));
    }

    [Fact]
    public void An_unmatched_statement_carrying_a_normative_marker_is_checkable_not_yet_built()
    {
        // "must" is a normative marker (RuleShapeCatalogue.Directive), but this phrasing fits none
        // of the five catalogue shapes.
        var statement = Statement("Commits must be signed.");
        var matching = RuleShapeCatalogue.MatchAll([statement]);

        var classify = RulesInventoryClassifier.BuildClassifier(matching);

        Assert.Equal(RuleStatementStatus.CheckableNotYetBuilt, classify(statement));
    }

    [Fact]
    public void An_unmatched_statement_carrying_no_normative_marker_is_not_a_rule()
    {
        var statement = Statement("Task → Read These First");
        var matching = RuleShapeCatalogue.MatchAll([statement]);

        var classify = RulesInventoryClassifier.BuildClassifier(matching);

        Assert.Equal(RuleStatementStatus.NotARule, classify(statement));
    }

    [Fact]
    public void A_statement_outside_the_matching_it_was_built_from_throws()
    {
        var matching = RuleShapeCatalogue.MatchAll([]);
        var classify = RulesInventoryClassifier.BuildClassifier(matching);

        Assert.Throws<InvalidOperationException>(() => classify(Statement("Never read secrets.env.")));
    }
}
