using AecoPostMortem.Rules;

namespace AecoPostMortem.Api.Tests;

/// <summary>
/// FR-40's classify function. Every statement the catalogue matched to a shape other than
/// <see cref="RuleShapeKind.PreferAOverB"/> is <see cref="RuleStatementStatus.CheckableNotYetBuilt"/>
/// — no built check watches those shapes against the real corpus yet (the same gap
/// <c>ToolVocabularyMismatchCheck</c> not being wired into <c>ApiHost.GetDigest</c> documents) — and
/// FR-34's own two unmatched dispositions map onto FR-40's remaining two statuses this piece can
/// answer honestly: <see cref="UnmatchedStatementDisposition.CheckableNotBuilt"/> is also
/// <see cref="RuleStatementStatus.CheckableNotYetBuilt"/>, and
/// <see cref="UnmatchedStatementDisposition.NotCheckable"/> is <see cref="RuleStatementStatus.NotARule"/>.
/// A <see cref="RuleShapeKind.PreferAOverB"/> match is the one shape this classifier now actually
/// watches: it resolves both operands against the real <see cref="ToolInvocationShape"/> corpus via
/// <see cref="OperandResolver.ResolveTwoOperands"/>, and reports <see cref="RuleStatementStatus.Watched"/>
/// only when both sides resolve to at least one real tool.
/// </summary>
public sealed class RulesInventoryClassifierTests
{
    static RuleStatement Statement(string text) => new() { SourceFile = "CLAUDE.md", Text = text };

    static Func<RuleStatement, RuleStatementStatus> Classify(
        RuleStatement statement, params ToolInvocationShape[] invocations)
    {
        var matching = RuleShapeCatalogue.MatchAll([statement]);
        return RulesInventoryClassifier.BuildClassifier(matching, invocations);
    }

    [Fact]
    public void A_statement_the_catalogue_matched_to_an_unwatched_shape_is_checkable_not_yet_built()
    {
        var statement = Statement("Never read secrets.env.");

        var classify = Classify(statement);

        Assert.Equal(RuleStatementStatus.CheckableNotYetBuilt, classify(statement));
    }

    [Fact]
    public void An_unmatched_statement_carrying_a_normative_marker_is_checkable_not_yet_built()
    {
        // "must" is a normative marker (RuleShapeCatalogue.Directive), but this phrasing fits none
        // of the five catalogue shapes.
        var statement = Statement("Commits must be signed.");

        var classify = Classify(statement);

        Assert.Equal(RuleStatementStatus.CheckableNotYetBuilt, classify(statement));
    }

    [Fact]
    public void An_unmatched_statement_carrying_no_normative_marker_is_not_a_rule()
    {
        var statement = Statement("Task → Read These First");

        var classify = Classify(statement);

        Assert.Equal(RuleStatementStatus.NotARule, classify(statement));
    }

    [Fact]
    public void A_statement_outside_the_matching_it_was_built_from_throws()
    {
        var matching = RuleShapeCatalogue.MatchAll([]);
        var classify = RulesInventoryClassifier.BuildClassifier(matching, []);

        Assert.Throws<InvalidOperationException>(() => classify(Statement("Never read secrets.env.")));
    }

    [Fact]
    public void A_prefer_a_over_b_match_whose_both_operands_resolve_against_the_real_corpus_is_watched()
    {
        var statement = Statement("Prefer rg over grep.");
        ToolInvocationShape[] invocations =
        [
            new() { ToolName = "rg", HasPattern = true },
            new() { ToolName = "grep", HasPattern = true },
        ];

        var classify = Classify(statement, invocations);

        Assert.Equal(RuleStatementStatus.Watched, classify(statement));
    }

    [Fact]
    public void A_prefer_a_over_b_match_whose_operand_never_resolves_stays_checkable_not_yet_built()
    {
        var statement = Statement("Prefer rg over grep.");
        // Neither "rg" nor "grep" was ever called, and neither name matches an MCP server field or a
        // ToolRole name — both operands fall all the way through to Unresolved.
        ToolInvocationShape[] invocations = [new() { ToolName = "view", HasPath = true }];

        var classify = Classify(statement, invocations);

        Assert.Equal(RuleStatementStatus.CheckableNotYetBuilt, classify(statement));
    }

    [Fact]
    public void A_prefer_a_over_b_match_against_an_empty_corpus_stays_checkable_not_yet_built()
    {
        var statement = Statement("Prefer rg over grep.");

        var classify = Classify(statement);

        Assert.Equal(RuleStatementStatus.CheckableNotYetBuilt, classify(statement));
    }

    [Fact]
    public void A_tool_is_banned_match_is_never_watched_by_this_classifier()
    {
        // ToolIsBanned's own remarks: turning a ban into a real verdict needs deciding which ToolRole
        // a banned tool "targets" for ToolVocabularyMismatchCheck, which nothing in this codebase has
        // ever decided — a real design question, not wired here.
        var statement = Statement("Never use curl.");
        ToolInvocationShape[] invocations = [new() { ToolName = "curl" }];

        var classify = Classify(statement, invocations);

        Assert.Equal(RuleStatementStatus.CheckableNotYetBuilt, classify(statement));
    }
}
