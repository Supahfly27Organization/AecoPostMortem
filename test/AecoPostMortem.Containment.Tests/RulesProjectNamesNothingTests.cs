using System.Text.Json;
using System.Text.RegularExpressions;

namespace AecoPostMortem.Containment.Tests;

/// <summary>
/// FR-34 / Repo Rule 6, S-25's second scenario: <b>no tool name, MCP server name or repository name
/// appears in <c>src/AecoPostMortem.Rules</c></b>. This is the one requirement the operator called
/// non-negotiable, so it is tested rather than reviewed.
///
/// <para>The test is an <b>allowlist of vocabulary</b>, not a blocklist of names, for the same reason
/// <see cref="SolutionContainmentTests.The_rules_project_references_no_persistence_assembly"/> is an
/// allowlist of zero references: a list of names to reject can never be exhaustive — it can only
/// reject the tools this author happened to know, which is precisely the assumption FR-34 exists to
/// remove. Instead, every word that appears in a string or character literal anywhere in that project
/// must be on <see cref="PermittedVocabulary"/>, a list of English grammar, FR-30's permitted
/// argument-field names, and regular-expression group names. A tool, MCP server or repository name
/// introduces a word that is none of those, and the build fails.</para>
///
/// <para>FR-34 is explicit that argument <i>field</i> names (<c>path</c>, <c>pattern</c>,
/// <c>old_str</c>) are permitted — "the invariant is about the vocabulary of a particular machine's
/// tools, not about the provider's event schema" — so those words are on the list by design.</para>
/// </summary>
public sealed class RulesProjectNamesNothingTests
{
    const string RulesProject = "src/AecoPostMortem.Rules";

    /// <summary>
    /// Every word permitted to appear inside a literal in the rules project, lower-cased. Adding a
    /// word here is the reviewable act this whole test exists to force: a reviewer confirms the new
    /// word is grammar, a regex construct or a provider field name — never the name of a tool, an MCP
    /// server or a repository.
    /// </summary>
    static readonly HashSet<string> PermittedVocabulary = new(StringComparer.OrdinalIgnoreCase)
    {
        // Regular-expression group names and constructs used to bound an operand.
        "body", "text", "ing", "op", "path", "pattern", "arg",

        // The verbs and particles the shapes match on — English grammar, not a vocabulary of tools.
        "prefer", "prefers", "preferring", "preferred", "favor", "favors", "favour", "favours",
        "over", "rather", "than", "instead", "of", "in", "preference",
        "never", "do", "does", "not", "don", "doesn", "must", "mustn", "cannot", "may", "shall",
        "should", "shouldn",
        "avoid", "refrain", "from", "without",
        "read", "reads", "reading", "open", "opens", "opening", "access", "accessing",
        "modify", "modifying", "edit", "editing", "write", "writing", "list", "listing",
        "use", "uses", "using", "call", "calls", "calling", "invoke", "invokes", "invoking",
        "run", "runs", "running", "query", "queries", "querying", "consult", "consulting",
        "reach", "reaches", "reaching", "spawn", "spawning", "start", "starting",
        "after", "before", "then", "first", "always",
        "pass", "passes", "passing", "specify", "specifies", "specifying",
        "include", "includes", "including", "provide", "provides", "providing",
        "supply", "supplies", "supplying", "set", "sets", "setting",
        "is", "are", "be", "banned", "forbidden", "prohibited", "disallowed", "allowed",
        "required", "require", "requires", "mandatory", "ensure", "only",
        "an", "the", "explicit", "every", "any", "all", "each",

        // Subordinating conjunctions and prepositions that end an operand's span.
        "when", "unless", "if", "because", "so", "while", "that", "which", "whose",
        "for", "and", "or", "but", "with", "as", "at", "on", "by", "into", "unlike",
        "to", "under", "no",

        // The words the two inventory reasons are written in. FR-40 requires a statement that matches
        // no shape to state why, so this project holds that much prose and no more.
        "statement", "directive", "shape", "catalogue", "matches", "states", "obligation",
        "check", "blank",

        // FR-40's status labels ("Watched", "Checkable — not yet built", "Not checkable") and the
        // prose of RulesInventory's own two exception/invariant messages.
        "watched", "checkable", "built", "yet", "fr", "extracted", "exactly", "status",
        "classifier", "returned", "none", "occurrence", "this", "corpus", "carried", "carries",
        "requested",

        // The nouns a shape's own name uses for what it binds.
        "param", "parameter", "parameters", "argument", "arguments",
        "flag", "flags", "option", "options", "tool", "tools", "command", "commands",
        "server", "repository", "file", "files", "directory",

        // The provider's own event-schema tag, which FR-26's extractor matches on. FR-34 permits it
        // in as many words: "the invariant is about the vocabulary of a particular machine's tools,
        // not about the provider's event schema."
        "custom", "instruction",

        // Identifiers referenced from inside an interpolation hole. These name a local, not a thing
        // in the world — but they are allowlisted one by one rather than stripped wholesale, so a
        // hole can never become a place to hide a name.
        "value", "length",

        // The prose of the two exception messages FR-28's refusal carries.
        "figure", "cannot", "computed", "across", "more", "than", "one", "rule", "version",
        "needs", "least", "session", "scoped",

        // The prose of FR-39's adjacency-refusal message (RuleSetVersionAdjacency).
        "comparison", "versions", "adjacent",
    };

    /// <summary>
    /// C# literals, in this order: raw string, verbatim string, regular string, character. Comments
    /// are matched too and then discarded, so a <c>//</c> or <c>/*</c> sequence <i>inside</i> a string
    /// cannot end the string early, and prose in a doc comment is not mistaken for a literal.
    /// </summary>
    static readonly Regex LiteralOrComment = new(
        """
        (?<raw>"{3,}[\s\S]*?"{3,})
        |(?<verbatim>@"(?:[^"]|"")*")
        |(?<regular>"(?:\\.|[^"\\\n])*")
        |(?<character>'(?:\\.|[^'\\\n])')
        |(?<comment>//[^\n]*|/\*[\s\S]*?\*/)
        """,
        RegexOptions.IgnorePatternWhitespace);

    /// <summary>A backslash escape (<c>\s</c>, <c>\t</c>, <c>\d</c>, <c>\p{L}</c>) is a regex or C#
    /// construct, never a name — its letters are stripped before words are counted.</summary>
    static readonly Regex Escape = new(@"\\[A-Za-z]");

    /// <summary>The two standard character-class ranges are regex syntax, and their endpoints are not
    /// letters of a word — without this, <c>[A-Za-z]</c> reads as the word "Za". Written out rather
    /// than as "any letter, hyphen, any letter", which would also eat the middle of a hyphenated
    /// English word and quietly weaken every check below it.</summary>
    static readonly Regex CharacterRange = new("A-Z|a-z", RegexOptions.CultureInvariant);

    /// <summary>Runs of two or more letters. A single letter cannot be the name of a tool, an MCP
    /// server or a repository, but it is exactly what a regex group name (<c>a</c>, <c>b</c>) or a
    /// leftover escape letter looks like.</summary>
    static readonly Regex Word = new("[A-Za-z]{2,}");

    [Fact]
    public void The_rules_project_uses_only_a_reviewed_vocabulary_in_its_literals()
    {
        var offenders = (
            from file in Repository.CSharpFiles(RulesProject)
            from literal in LiteralsIn(File.ReadAllText(file.FullName))
            let scannable = CharacterRange.Replace(Escape.Replace(literal, " "), " ")
            from word in Word.Matches(scannable).Select(match => match.Value)
            where !PermittedVocabulary.Contains(word)
            select $"{Repository.RelativePath(file)}: \"{word}\" (in {Truncate(literal)})")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            "Nothing in src/AecoPostMortem.Rules may name a tool, an MCP server or a repository "
            + "(FR-34, Repo Rule 6). Each word below appears in a literal there and is not on "
            + $"{nameof(PermittedVocabulary)}. If it is grammar, a regex construct or one of FR-30's "
            + "argument field names, add it to that list — deliberately, as the reviewable act this "
            + "test exists to force. If it is the name of a tool, a server or a repository, the "
            + "invariant forbids it: resolve it through ToolVocabulary/ToolRoleDeriver/OperandResolver "
            + "from the corpus instead. Found:\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// The vocabulary check reads literals; this one reads the whole file, so a name that entered as a
    /// type name, a member name or a comment is caught too. Its forbidden vocabulary is <i>derived</i>
    /// from the frozen corpus manifest's own repository names rather than written by hand — the only
    /// real name vocabulary this repository holds.
    ///
    /// <para>Only the <c>repository</c> fields are used, not the recorded <c>cwd</c> paths: a cwd
    /// tokenises to ordinary words (<c>git</c>, <c>local</c>, <c>temp</c>) that would make this check
    /// fire on unrelated code, which would make it noise rather than evidence.</para>
    /// </summary>
    [Fact]
    public void The_rules_project_names_no_repository_the_frozen_corpus_names()
    {
        var names = RepositoryNamesInTheFrozenCorpus();

        Assert.NotEmpty(names);

        var offenders = (
            from file in Repository.CSharpFiles(RulesProject)
            let source = File.ReadAllText(file.FullName)
            from name in names
            where source.Contains(name, StringComparison.OrdinalIgnoreCase)
            select $"{Repository.RelativePath(file)}: \"{name}\"").ToArray();

        Assert.True(
            offenders.Length == 0,
            "src/AecoPostMortem.Rules names a repository from the frozen reference corpus "
            + "(fixtures/corpus-manifest.json); FR-34 forbids it. Found: "
            + string.Join(", ", offenders));
    }

    /// <summary>
    /// S-25's third scenario, second half: the rules project's checks "run against inputs passed to
    /// them, with no database in the test". The project itself referencing nothing is asserted by
    /// <see cref="SolutionContainmentTests.The_rules_project_references_no_persistence_assembly"/>;
    /// this asserts the same of the test project, so the checks cannot be exercised through a store
    /// even indirectly.
    /// </summary>
    [Fact]
    public void The_rules_test_project_reaches_no_store_either()
    {
        const string testProject = "test/AecoPostMortem.Rules.Tests/AecoPostMortem.Rules.Tests.csproj";
        var project = Repository.ProjectFile(testProject);

        var offenders = Repository.References(project, "PackageReference")
            .Concat(Repository.References(project, "ProjectReference"))
            .Concat(Repository.References(project, "Reference"))
            .Where(reference => !IsTestHarnessOrTheSubject(reference.Value))
            .Select(reference => Repository.Describe(testProject, reference))
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            "AecoPostMortem.Rules.Tests may reference only the test harness and the rules project "
            + "itself: S-25 requires the checks to be exercised with no database in the test, and a "
            + "reference to any other assembly is how one would arrive. Found: "
            + string.Join(", ", offenders));
    }

    static bool IsTestHarnessOrTheSubject(string reference) =>
        reference.StartsWith("xunit", StringComparison.OrdinalIgnoreCase)
        || reference.StartsWith("Microsoft.NET.Test.Sdk", StringComparison.OrdinalIgnoreCase)
        || reference.EndsWith("AecoPostMortem.Rules.csproj", StringComparison.OrdinalIgnoreCase);

    static IEnumerable<string> LiteralsIn(string source) =>
        from Match match in LiteralOrComment.Matches(source)
        where !match.Groups["comment"].Success
        select match.Value;

    static string Truncate(string literal) =>
        literal.Length <= 60 ? literal : literal[..60] + "…";

    static IReadOnlyList<string> RepositoryNamesInTheFrozenCorpus()
    {
        var manifest = Path.Combine(Repository.Root.FullName, "fixtures", "corpus-manifest.json");
        using var document = JsonDocument.Parse(File.ReadAllText(manifest));

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var repository in document.RootElement
                     .GetProperty("totals").GetProperty("repositories").EnumerateArray())
        {
            foreach (var segment in (repository.GetString() ?? string.Empty).Split('/'))
            {
                if (segment.Length >= 4)
                {
                    names.Add(segment);
                }
            }
        }

        return names.Order(StringComparer.Ordinal).ToArray();
    }
}
