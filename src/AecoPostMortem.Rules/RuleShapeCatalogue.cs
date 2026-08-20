using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace AecoPostMortem.Rules;

/// <summary>
/// FR-34 (S-25, issue #39): the catalogue of check shapes. It matches a rule statement on
/// <b>shape alone</b> — the grammar of an obligation — and takes the operands out of that
/// statement's own text, so a repository whose rules this product's author never saw is checked by
/// the same five shapes as one they did.
///
/// <para>Nothing here names a tool, an MCP server or a repository, and nothing here names a rule.
/// The patterns are English verbs and particles; the operands are whatever text the matched
/// statement put between them. <c>RulesProjectNamesNothingTests</c> in
/// <c>test/AecoPostMortem.Containment.Tests</c> proves it by requiring every word in every literal
/// in this project to be on a reviewed vocabulary of grammar and provider field names.</para>
/// </summary>
public static class RuleShapeCatalogue
{
    /// <summary>One catalogue entry: a shape and one phrasing of it. A shape with two phrasings has
    /// two entries, which is how "use A after B" and "use B before A" reach the same operand
    /// positions — the second entry simply names its groups the other way round.</summary>
    sealed record Entry(RuleShapeKind Kind, Regex Pattern);

    const RegexOptions Options =
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;

    /// <summary>
    /// The catalogue, in the order it is tried. Precedence runs most specific to least: a prohibition
    /// whose object is a path, then an explicit comparison, then a bare prohibition, then an
    /// ordering, then an obligation on an argument. The order matters — "do not use A instead of B"
    /// carries both a prohibition and a comparison, and reading it as the comparison it is spelled
    /// out as loses less than reading it as a ban on the whole phrase.
    /// </summary>
    static readonly Entry[] Catalogue =
    [
        new(RuleShapeKind.NeverReadPath, new Regex(
            @"\b(?:never|do\s+not|don't|must\s+not|cannot|may\s+not)\s+"
            + @"(?:read|reads|reading|open|opens|opening|access|accessing"
            + @"|modify|modifying|edit|editing|list|listing)\s+"
            + @"(?<a>.+?)\s*(?:[.;](?=\s|$)|$)",
            Options)),

        new(RuleShapeKind.PreferAOverB, new Regex(
            @"\b(?:prefer|prefers|preferring|favor|favors|favour|favours)\s+"
            + @"(?:[a-z]+ing\s+)?(?<a>.+?)\s+(?:over|rather\s+than|instead\s+of)\s+"
            + @"(?:[a-z]+ing\s+)?(?<b>.+?)\s*(?:[.;](?=\s|$)|$)",
            Options)),

        // The negative lookbehinds are load-bearing: "do not use A instead of B" is a prohibition,
        // not a preference, and without them this entry would read it as the opposite of what it says.
        new(RuleShapeKind.PreferAOverB, new Regex(
            @"(?<!\bnot\s)(?<!\bnever\s)\b(?:use|uses|using)\s+(?<a>.+?)\s+"
            + @"(?:instead\s+of|rather\s+than|in\s+preference\s+to)\s+"
            + @"(?<b>.+?)\s*(?:[.;](?=\s|$)|$)",
            Options)),

        new(RuleShapeKind.ToolIsBanned, new Regex(
            @"\b(?:never|do\s+not|don't|must\s+not|cannot|may\s+not|avoid|refrain\s+from)\s+"
            + @"(?:use|uses|using|call|calls|calling|invoke|invokes|invoking"
            + @"|run|runs|running|query|queries|querying)\s+"
            + @"(?<a>.+?)\s*(?:[.;,—](?=\s|$)|$)",
            Options)),

        new(RuleShapeKind.ToolIsBanned, new Regex(
            @"^\s*(?<a>.+?)\s+(?:is|are)\s+"
            + @"(?:banned|forbidden|prohibited|disallowed|not\s+allowed)\b",
            Options)),

        new(RuleShapeKind.UseAAfterB, new Regex(
            @"\b(?:use|uses|using|call|calls|calling|invoke|invokes|invoking"
            + @"|run|runs|running|query|queries|querying|consult|consulting)\s+"
            + @"(?<a>.+?)\s+after\s+(?<b>.+?)\s*(?:[.;](?=\s|$)|$)",
            Options)),

        new(RuleShapeKind.UseAAfterB, new Regex(
            @"\b(?:use|uses|using|call|calls|calling|invoke|invokes|invoking"
            + @"|run|runs|running|query|queries|querying|consult|consulting)\s+"
            + @"(?<b>.+?)\s+before\s+(?<a>.+?)\s*(?:[.;](?=\s|$)|$)",
            Options)),

        new(RuleShapeKind.AlwaysPassParam, new Regex(
            @"\balways\s+(?:pass|passes|passing|specify|specifies|specifying"
            + @"|include|includes|including|provide|provides|providing"
            + @"|supply|supplies|supplying|set|sets|setting)\s+"
            + @"(?:an?\s+|the\s+)?(?:explicit\s+)?(?<a>.+?)\s*(?:[.;](?=\s|$)|$)",
            Options)),
    ];

    /// <summary>
    /// Whether a statement expresses an obligation at all. This is the difference between FR-40's
    /// "Checkable — not yet built" and its "Not checkable": a statement carrying a normative marker
    /// is a rule whose phrasing this catalogue does not yet cover, while one carrying none is prose
    /// that no shape could ever check. The markers are English modals, not a list of rules.
    /// </summary>
    static readonly Regex Directive = new(
        @"\b(?:never|always|must|should|shall|cannot|do\s+not|don't|may\s+not"
        + @"|avoid|refrain|only|ensure|require|required|requires|mandatory"
        + @"|banned|forbidden|prohibited|disallowed|prefer|prefers)\b",
        Options);

    const string NoShapeMatches = "the statement is a directive that no shape in the catalogue matches";
    const string NoObligation = "the statement states no obligation to check";
    const string Blank = "the statement is blank";

    /// <summary>The shapes this catalogue holds, in the order they are tried — the denominator a
    /// coverage figure is stated against, so it is published rather than left to be counted.</summary>
    public static IReadOnlyList<RuleShapeKind> Shapes { get; } =
        Catalogue.Select(entry => entry.Kind).Distinct().ToArray();

    /// <summary>
    /// Matches one statement against the catalogue, returning the first shape whose phrasing fits and
    /// whose operands survive normalisation. False means no shape matched — call
    /// <see cref="MatchAll"/> to get that statement's disposition and reason rather than only its
    /// absence.
    /// </summary>
    public static bool TryMatch(RuleStatement statement, [MaybeNullWhen(false)] out RuleShapeMatch match)
    {
        ArgumentNullException.ThrowIfNull(statement);

        foreach (var entry in Catalogue)
        {
            // Every match of this phrasing, not only the first: a statement can satisfy a pattern at
            // one position with an operand the shape will not accept and at a later one with an
            // operand it will, and stopping at the first would report the statement as unmatched on
            // the strength of a position it had already moved past.
            for (var found = entry.Pattern.Match(statement.Text); found.Success; found = found.NextMatch())
            {
                var operandA = RuleOperandText.Normalize(found.Groups["a"].Value);
                if (operandA.Length == 0 || !OperandSuitsShape(entry.Kind, operandA))
                {
                    continue;
                }

                string? operandB = null;
                if (found.Groups["b"].Success)
                {
                    operandB = RuleOperandText.Normalize(found.Groups["b"].Value);
                    if (operandB.Length == 0)
                    {
                        continue;
                    }
                }

                match = new RuleShapeMatch
                {
                    Statement = statement,
                    Kind = entry.Kind,
                    OperandAText = operandA,
                    OperandBText = operandB,
                };
                return true;
            }
        }

        match = null;
        return false;
    }

    /// <summary>
    /// Matches every statement, partitioning them into <see cref="RuleShapeMatching.Matches"/> and
    /// <see cref="RuleShapeMatching.Unmatched"/>. Nothing is filtered: a statement that matches no
    /// shape leaves as an <see cref="UnmatchedStatement"/> with a disposition and a reason, which is
    /// S-25's fourth scenario and FR-40's inventory in one step.
    /// </summary>
    public static RuleShapeMatching MatchAll(IEnumerable<RuleStatement> statements)
    {
        ArgumentNullException.ThrowIfNull(statements);

        var matches = new List<RuleShapeMatch>();
        var unmatched = new List<UnmatchedStatement>();

        foreach (var statement in statements)
        {
            ArgumentNullException.ThrowIfNull(statement);

            if (TryMatch(statement, out var match))
            {
                matches.Add(match);
                continue;
            }

            unmatched.Add(new UnmatchedStatement
            {
                Statement = statement,
                Disposition = DispositionOf(statement),
                Reason = ReasonFor(statement),
            });
        }

        return new RuleShapeMatching { Matches = matches, Unmatched = unmatched };
    }

    /// <summary>
    /// A path-shaped operand belongs to <see cref="RuleShapeKind.NeverReadPath"/> and a name-shaped
    /// one to <see cref="RuleShapeKind.ToolIsBanned"/>. Failing this test does not consume the
    /// statement — it falls through to the next entry, and out to the inventory if none fits, rather
    /// than being recorded under a shape whose operand it does not have.
    /// </summary>
    static bool OperandSuitsShape(RuleShapeKind kind, string operandA) => kind switch
    {
        RuleShapeKind.NeverReadPath => RuleOperandText.LooksLikePath(operandA),
        RuleShapeKind.ToolIsBanned => !RuleOperandText.LooksLikePath(operandA),
        RuleShapeKind.AlwaysPassParam => RuleOperandText.LooksLikeParameterName(operandA),
        _ => true,
    };

    static UnmatchedStatementDisposition DispositionOf(RuleStatement statement) =>
        Directive.IsMatch(statement.Text)
            ? UnmatchedStatementDisposition.CheckableNotBuilt
            : UnmatchedStatementDisposition.NotCheckable;

    static string ReasonFor(RuleStatement statement)
    {
        if (string.IsNullOrWhiteSpace(statement.Text))
        {
            return Blank;
        }

        return Directive.IsMatch(statement.Text) ? NoShapeMatches : NoObligation;
    }
}
