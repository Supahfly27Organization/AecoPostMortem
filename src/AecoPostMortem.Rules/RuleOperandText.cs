using System.Text.RegularExpressions;

namespace AecoPostMortem.Rules;

/// <summary>
/// Reduces the raw span a shape's pattern captured to the operand the statement was naming. Every
/// rule here is <b>grammar</b> — a code span, an article, a gerund, a subordinate clause, a role
/// noun — and never a vocabulary of tools, MCP servers or repositories: FR-34 permits the provider's
/// own schema words and ordinary English, and forbids exactly the three kinds of name this file
/// contains none of.
/// </summary>
public static class RuleOperandText
{
    /// <summary>A backticked or double-quoted span. Operator prose overwhelmingly marks the thing a
    /// rule is about as code, so when one is present it <i>is</i> the operand and everything around
    /// it is the sentence.</summary>
    static readonly Regex CodeSpan = new(
        @"`(?<op>[^`]+)`|""(?<op>[^""]+)""",
        RegexOptions.CultureInvariant);

    /// <summary>A subordinate clause the shape's own keywords could not exclude — "…&#160;when
    /// dispatching", "…&#160;for dependency chains". It qualifies the obligation rather than naming
    /// what the obligation is about.</summary>
    static readonly Regex TrailingClause = new(
        @"\s+(?:when|unless|if|because|so|while|that|which|for|to|and|or|but|under|with"
        + @"|instead\s+of|rather\s+than)\b.*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    static readonly Regex LeadingArticle = new(
        @"^(?:an?|the)\s+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>A leading gerund — "querying&#160;…", "reading&#160;…". Deliberately case-sensitive:
    /// a capitalised word ending in those letters is far more likely to be a name than a verb, and
    /// this is not permitted to eat one.</summary>
    static readonly Regex LeadingGerund = new(
        @"^[a-z]+ing\s+",
        RegexOptions.CultureInvariant);

    /// <summary>The noun naming what kind of thing the operand is, which the shape already knows —
    /// "an explicit model param" names <c>model</c>.</summary>
    static readonly Regex TrailingRoleNoun = new(
        @"\s+(?:param|parameter|parameters|argument|arguments|flag|flags|option|options)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Sentence punctuation and quoting. <c>/</c>, <c>\</c> and <c>-</c> are absent on
    /// purpose: a trailing slash is part of a directory operand, not decoration around it.</summary>
    static readonly char[] Punctuation =
        ['.', ',', ';', ':', '!', '?', '`', '"', '\'', '(', ')', '[', ']', '{', '}'];

    /// <summary>Anything carrying a separator, or ending in a short extension.</summary>
    static readonly Regex FileExtension = new(
        @"^\S+\.[A-Za-z]{1,6}$",
        RegexOptions.CultureInvariant);

    public static string Normalize(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);

        var text = raw.Trim();

        var code = CodeSpan.Match(text);
        if (code.Success)
        {
            return code.Groups["op"].Value.Trim();
        }

        text = TrailingClause.Replace(text, string.Empty);
        text = LeadingArticle.Replace(text, string.Empty);
        text = LeadingGerund.Replace(text, string.Empty);
        text = TrailingRoleNoun.Replace(text, string.Empty);

        return text.Trim().Trim(Punctuation).Trim();
    }

    /// <summary>
    /// Whether an operand is shaped like a location rather than a name. This is a test of the
    /// operand's own characters — a separator, or a short extension — not a comparison against any
    /// path this product knows, which it must not know.
    /// </summary>
    public static bool LooksLikePath(string operandText)
    {
        ArgumentNullException.ThrowIfNull(operandText);

        return operandText.Contains('/', StringComparison.Ordinal)
               || operandText.Contains('\\', StringComparison.Ordinal)
               || FileExtension.IsMatch(operandText);
    }

    /// <summary>
    /// Whether an operand is shaped like a single argument key rather than a clause. "Pass" is
    /// grammatically ambiguous — this product's own live corpus carries a real statement using it to
    /// mean "pass a CI check" ("always pass build and type checks..."), not "pass an argument" — and a
    /// real JSON argument key is always one token (this product's own verified field names: <c>pattern</c>,
    /// <c>old_str</c>, <c>file_text</c>). A multi-word capture cannot be a key, so it is rejected here
    /// rather than matched with confidence it does not deserve.
    /// </summary>
    public static bool LooksLikeParameterName(string operandText)
    {
        ArgumentNullException.ThrowIfNull(operandText);

        return operandText.Length > 0 && !operandText.Any(char.IsWhiteSpace);
    }
}
