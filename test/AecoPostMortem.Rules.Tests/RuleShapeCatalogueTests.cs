namespace AecoPostMortem.Rules.Tests;

/// <summary>
/// S-25 (issue #39, FR-34). Every statement in this file is a plain string handed to the catalogue —
/// there is no database, no store and no fixture file anywhere in it, which is the second half of
/// the story's third scenario ("its checks run against inputs passed to them, with no database in
/// the test").
/// </summary>
public sealed class RuleShapeCatalogueTests
{
    static RuleStatement Statement(string text) =>
        new() { SourceFile = "CLAUDE.md", Text = text };

    /// <summary>
    /// A fictional repository's rules, using tool names that exist nowhere — not in this product, not
    /// in the reference corpus, not on any machine. Matching these proves the catalogue matches on
    /// shape alone: no name here could have been hard-coded, because none of them existed before this
    /// file was written.
    /// </summary>
    static readonly string[] RulesOfARepositoryThisAuthorNeverSaw =
    [
        "Prefer `zzyzx-index` over `plodder-scan` when locating a symbol.",
        "Use `quill-fetch` instead of `plodder-scan` for cross-file edits.",
        "Prefer querying `zzyzx-index` rather than `wumpus-grep` for dependency chains.",
        "Never read `vendor/quux-engine/generated/` unless the task is about code generation.",
        "Do not open `.secrets/keyring.toml` under any circumstances.",
        "Never use `frobnicate` — it is not available in this environment.",
        "Use `snippet-fetch` after `graph-probe` when tracing a call chain.",
        "Always pass an explicit `tier` param when dispatching a subagent.",
    ];

    // ---- Scenario: A shape matches a rule it was never written for ----

    [Fact]
    public void A_shape_matches_a_rule_from_a_repository_outside_the_reference_corpus()
    {
        var statement = Statement("Prefer `zzyzx-index` over `plodder-scan` when locating a symbol.");

        Assert.True(RuleShapeCatalogue.TryMatch(statement, out var match));
        Assert.Equal(RuleShapeKind.PreferAOverB, match.Kind);
        Assert.Equal("zzyzx-index", match.OperandAText);
        Assert.Equal("plodder-scan", match.OperandBText);
    }

    [Fact]
    public void Operands_are_taken_from_the_matched_statements_own_text()
    {
        var first = Statement("Prefer `alpha-one` over `beta-two` when in doubt.");
        var second = Statement("Prefer `gamma-three` over `delta-four` when in doubt.");

        Assert.True(RuleShapeCatalogue.TryMatch(first, out var a));
        Assert.True(RuleShapeCatalogue.TryMatch(second, out var b));

        Assert.Equal(RuleShapeKind.PreferAOverB, a.Kind);
        Assert.Equal(RuleShapeKind.PreferAOverB, b.Kind);
        Assert.Equal("alpha-one", a.OperandAText);
        Assert.Equal("beta-two", a.OperandBText);
        Assert.Equal("gamma-three", b.OperandAText);
        Assert.Equal("delta-four", b.OperandBText);
    }

    [Fact]
    public void A_never_read_path_rule_yields_the_path_from_its_own_text()
    {
        var statement = Statement(
            "Never read `vendor/quux-engine/generated/` unless the task is about code generation.");

        Assert.True(RuleShapeCatalogue.TryMatch(statement, out var match));
        Assert.Equal(RuleShapeKind.NeverReadPath, match.Kind);
        Assert.Equal("vendor/quux-engine/generated/", match.OperandAText);
        Assert.Null(match.OperandBText);
    }

    /// <summary>
    /// An operand's own full stop is not the sentence's. A path with a dotted segment, or a dotfile,
    /// must survive whole — truncating it produces a confident wrong operand, which is exactly the
    /// failure mode S-25's edge case says must be caught by construction rather than by review.
    /// </summary>
    [Theory]
    [InlineData(
        "Never read `vendor/Quux.Engine/Migrations/` unless the task is about migrations.",
        "vendor/Quux.Engine/Migrations/")]
    [InlineData(
        "Do not open `.secrets/keyring.toml` under any circumstances.",
        ".secrets/keyring.toml")]
    public void An_operand_is_not_truncated_at_a_full_stop_inside_it(string text, string expected)
    {
        Assert.True(RuleShapeCatalogue.TryMatch(Statement(text), out var match), text);
        Assert.Equal(RuleShapeKind.NeverReadPath, match.Kind);
        Assert.Equal(expected, match.OperandAText);
    }

    [Fact]
    public void A_banned_tool_rule_yields_the_banned_name_from_its_own_text()
    {
        var statement = Statement("Never use `frobnicate` — it is not available in this environment.");

        Assert.True(RuleShapeCatalogue.TryMatch(statement, out var match));
        Assert.Equal(RuleShapeKind.ToolIsBanned, match.Kind);
        Assert.Equal("frobnicate", match.OperandAText);
        Assert.Null(match.OperandBText);
    }

    [Fact]
    public void An_ordering_rule_yields_both_operands_from_its_own_text()
    {
        var statement = Statement("Use `snippet-fetch` after `graph-probe` when tracing a call chain.");

        Assert.True(RuleShapeCatalogue.TryMatch(statement, out var match));
        Assert.Equal(RuleShapeKind.UseAAfterB, match.Kind);
        Assert.Equal("snippet-fetch", match.OperandAText);
        Assert.Equal("graph-probe", match.OperandBText);
    }

    /// <summary>
    /// "Use B before A" and "use A after B" are the same ordering fact phrased two ways, so both
    /// normalise to the same operand positions: A is always the later step.
    /// </summary>
    [Fact]
    public void The_before_phrasing_of_an_ordering_rule_normalises_to_the_same_operand_positions()
    {
        var after = Statement("Use `snippet-fetch` after `graph-probe`.");
        var before = Statement("Use `graph-probe` before `snippet-fetch`.");

        Assert.True(RuleShapeCatalogue.TryMatch(after, out var fromAfter));
        Assert.True(RuleShapeCatalogue.TryMatch(before, out var fromBefore));

        Assert.Equal(RuleShapeKind.UseAAfterB, fromBefore.Kind);
        Assert.Equal(fromAfter.OperandAText, fromBefore.OperandAText);
        Assert.Equal(fromAfter.OperandBText, fromBefore.OperandBText);
    }

    [Fact]
    public void A_mandatory_parameter_rule_yields_the_parameter_from_its_own_text()
    {
        var statement = Statement("Always pass an explicit `tier` param when dispatching a subagent.");

        Assert.True(RuleShapeCatalogue.TryMatch(statement, out var match));
        Assert.Equal(RuleShapeKind.AlwaysPassParam, match.Kind);
        Assert.Equal("tier", match.OperandAText);
        Assert.Null(match.OperandBText);
    }

    /// <summary>
    /// An operand that is not backticked still comes out of the statement's own text: the shape's own
    /// keywords bound it, and only grammar — an article, a gerund, a trailing subordinate clause — is
    /// trimmed off what is left.
    /// </summary>
    [Fact]
    public void An_operand_that_carries_no_code_span_is_still_read_from_the_statement()
    {
        var statement = Statement("prefer querying zzyzx-index over generic file search");

        Assert.True(RuleShapeCatalogue.TryMatch(statement, out var match));
        Assert.Equal(RuleShapeKind.PreferAOverB, match.Kind);
        Assert.Equal("zzyzx-index", match.OperandAText);
        Assert.Equal("generic file search", match.OperandBText);
    }

    // ---- The measured floor: five shapes, eight rules, nothing hard-coded ----

    [Fact]
    public void The_catalogue_reproduces_the_measured_floor_of_eight_rules_across_five_shapes()
    {
        var matching = RuleShapeCatalogue.MatchAll(
            RulesOfARepositoryThisAuthorNeverSaw.Select(Statement));

        Assert.True(
            matching.Matches.Count >= 8,
            "FR-34's measured floor is 8 rules checkable with nothing hard-coded; matched "
            + $"{matching.Matches.Count}: "
            + string.Join(" | ", matching.Unmatched.Select(u => u.Statement.Text)));

        Assert.Equal(5, matching.Matches.Select(match => match.Kind).Distinct().Count());
    }

    [Fact]
    public void The_catalogue_holds_the_five_measured_shapes()
    {
        RuleShapeKind[] expected =
        [
            RuleShapeKind.NeverReadPath,
            RuleShapeKind.PreferAOverB,
            RuleShapeKind.ToolIsBanned,
            RuleShapeKind.UseAAfterB,
            RuleShapeKind.AlwaysPassParam,
        ];

        Assert.Equal(expected, RuleShapeCatalogue.Shapes);
    }

    /// <summary>
    /// The same statements this repository's own instruction files carry. This repository is outside
    /// the frozen reference corpus (which holds the UpFront repositories only, see
    /// <c>fixtures/corpus-manifest.json</c>), so these are rules the shapes were not written against
    /// either — and they are real operator prose rather than prose shaped to fit.
    /// </summary>
    [Theory]
    [InlineData(
        "prefer querying codebase-memory-mcp over generic file search",
        RuleShapeKind.PreferAOverB)]
    [InlineData(
        "Never read `src/AecoPostMortem.Data/Migrations/` unless the task is explicitly about migrations.",
        RuleShapeKind.NeverReadPath)]
    [InlineData(
        "Do not use Serena for navigation/search",
        RuleShapeKind.ToolIsBanned)]
    [InlineData(
        "Query Codebase Memory before reading files.",
        RuleShapeKind.UseAAfterB)]
    [InlineData(
        "always pass an explicit model param when dispatching",
        RuleShapeKind.AlwaysPassParam)]
    public void Shapes_fire_on_this_repositorys_own_rules(string text, RuleShapeKind expected)
    {
        Assert.True(RuleShapeCatalogue.TryMatch(Statement(text), out var match), text);
        Assert.Equal(expected, match.Kind);
        Assert.NotEqual(string.Empty, match.OperandAText);
    }

    /// <summary>
    /// "Pass" is ambiguous: this project's own live corpus carries a real statement using "pass" to
    /// mean "pass a CI check" ("always pass build and type checks..."), not "pass an argument". A
    /// multi-word capture cannot be a JSON argument key, so it must not be read as one — the operand
    /// is rejected and the statement falls through un-shaped, the same "a rejected operand does not
    /// consume the statement" precedent <c>NeverReadPath</c>/<c>ToolIsBanned</c> already established.
    /// </summary>
    [Fact]
    public void A_multi_word_operand_does_not_fit_the_parameter_obligation_shape()
    {
        var statement = Statement("Always pass full stack traces on every failure.");

        var matching = RuleShapeCatalogue.MatchAll([statement]);

        Assert.Empty(matching.Matches);
        var unmatched = Assert.Single(matching.Unmatched);
        Assert.Equal(UnmatchedStatementDisposition.CheckableNotBuilt, unmatched.Disposition);
    }

    // ---- Scenario: A rule matching no shape is not silently dropped ----

    [Fact]
    public void A_directive_that_matches_no_built_shape_is_recorded_as_checkable_not_built()
    {
        var statement = Statement("Never commit a file larger than five megabytes.");

        var matching = RuleShapeCatalogue.MatchAll([statement]);

        var unmatched = Assert.Single(matching.Unmatched);
        Assert.Equal(UnmatchedStatementDisposition.CheckableNotBuilt, unmatched.Disposition);
        Assert.False(string.IsNullOrWhiteSpace(unmatched.Reason));
    }

    [Fact]
    public void A_statement_that_states_no_obligation_is_recorded_as_not_checkable()
    {
        var statement = Statement("This module holds the ingest pipeline.");

        var matching = RuleShapeCatalogue.MatchAll([statement]);

        var unmatched = Assert.Single(matching.Unmatched);
        Assert.Equal(UnmatchedStatementDisposition.NotCheckable, unmatched.Disposition);
        Assert.False(string.IsNullOrWhiteSpace(unmatched.Reason));
    }

    [Fact]
    public void Every_unmatched_statement_carries_a_reason()
    {
        string[] texts =
        [
            "Never commit a file larger than five megabytes.",
            "This module holds the ingest pipeline.",
            "Keep the changelog in reverse chronological order.",
            "",
        ];

        var matching = RuleShapeCatalogue.MatchAll(texts.Select(Statement));

        Assert.All(matching.Unmatched, unmatched =>
            Assert.False(string.IsNullOrWhiteSpace(unmatched.Reason)));
    }

    [Fact]
    public void No_statement_is_dropped_between_the_input_and_the_matching_result()
    {
        string[] texts =
        [
            "Prefer `zzyzx-index` over `plodder-scan` when locating a symbol.",
            "Never commit a file larger than five megabytes.",
            "This module holds the ingest pipeline.",
            "Always pass an explicit `tier` param when dispatching a subagent.",
        ];

        var matching = RuleShapeCatalogue.MatchAll(texts.Select(Statement));

        Assert.Equal(texts.Length, matching.StatementCount);
        Assert.Equal(
            texts.Order(StringComparer.Ordinal),
            matching.Matches.Select(match => match.Statement.Text)
                .Concat(matching.Unmatched.Select(unmatched => unmatched.Statement.Text))
                .Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_statement_appears_either_as_a_match_or_as_unmatched_never_as_both()
    {
        var matching = RuleShapeCatalogue.MatchAll(
            RulesOfARepositoryThisAuthorNeverSaw.Select(Statement));

        var matched = matching.Matches.Select(match => match.Statement.Text).ToHashSet(StringComparer.Ordinal);
        var unmatched = matching.Unmatched.Select(u => u.Statement.Text).ToHashSet(StringComparer.Ordinal);

        Assert.Empty(matched.Intersect(unmatched, StringComparer.Ordinal));
    }

    // ---- The catalogue's own surface ----

    /// <summary>
    /// A matched operand is text, and resolving it to tools is <see cref="OperandResolver"/>'s job —
    /// this is the seam S-26 (FR-35, "this rule names a tool your agent does not have") builds on, so
    /// it is asserted here rather than left to be discovered.
    /// </summary>
    [Fact]
    public void A_matched_operand_feeds_the_operand_resolver_unchanged()
    {
        var statement = Statement("Never use `frobnicate` — it is not available in this environment.");

        Assert.True(RuleShapeCatalogue.TryMatch(statement, out var match));

        var resolved = OperandResolver.Resolve(match.OperandAText, []);

        Assert.Equal(OperandResolutionLayer.Unresolved, resolved.Layer);
        Assert.Equal("frobnicate", resolved.OperandText);
    }

    [Fact]
    public void Matching_the_same_statement_twice_gives_the_same_answer()
    {
        var statement = Statement("Use `snippet-fetch` after `graph-probe` when tracing a call chain.");

        Assert.True(RuleShapeCatalogue.TryMatch(statement, out var first));
        Assert.True(RuleShapeCatalogue.TryMatch(statement, out var second));

        Assert.Equal(first, second);
    }

    [Fact]
    public void An_empty_statement_matches_nothing_and_is_still_reported()
    {
        var matching = RuleShapeCatalogue.MatchAll([Statement("   ")]);

        Assert.Empty(matching.Matches);
        var unmatched = Assert.Single(matching.Unmatched);
        Assert.Equal(UnmatchedStatementDisposition.NotCheckable, unmatched.Disposition);
    }

    [Fact]
    public void Matching_rejects_a_null_statement_rather_than_dropping_it()
    {
        Assert.Throws<ArgumentNullException>(() => RuleShapeCatalogue.MatchAll(null!));
    }
}
