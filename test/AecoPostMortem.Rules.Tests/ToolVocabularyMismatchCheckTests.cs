namespace AecoPostMortem.Rules.Tests;

/// <summary>Issue #40 (S-26, FR-35): flags a rule statement that names a tool your agent does not
/// have — either the named tool does not exist anywhere in the corpus, or it exists but is not the
/// dominant tool of the role the rule targets. Built on S-23's <see cref="OperandResolver"/> and
/// S-21's <see cref="ToolRoleDeriver"/>.</summary>
public sealed class ToolVocabularyMismatchCheckTests
{
    [Fact]
    public void A_minor_tool_named_in_its_role_is_flagged_with_both_tools_and_both_counts()
    {
        // The navigation-rule edge case named in issue #40: a rule names a tool used a measured 129
        // times while the tool actually doing that job in the same role is used 1,346 times and is
        // never named.
        ToolInvocationShape[] invocations =
        [
            .. Calls("minor-search-tool", 129, hasPattern: true),
            .. Calls("dominant-search-tool", 1346, hasPattern: true),
        ];
        var mentions = new[]
        {
            new RuleToolMention
            {
                RuleText = "Always use minor-search-tool before broad file search.",
                NamedTool = "minor-search-tool",
                TargetRole = ToolRole.Search,
            },
        };

        var results = ToolVocabularyMismatchCheck.Run(mentions, invocations);

        var mismatch = Assert.Single(results);
        var minor = Assert.IsType<MinorToolNamed>(mismatch);
        Assert.Equal("minor-search-tool", minor.NamedTool);
        Assert.Equal(129, minor.NamedToolCallCount);
        Assert.Equal("dominant-search-tool", minor.DominantTool);
        Assert.Equal(1346, minor.DominantToolCallCount);
        Assert.Equal(ToolRole.Search, minor.TargetRole);
    }

    [Fact]
    public void A_rule_naming_a_tool_that_does_not_exist_is_flagged_distinctly()
    {
        ToolInvocationShape[] invocations = [.. Calls("real-tool", 5, hasPattern: true)];
        var mentions = new[]
        {
            new RuleToolMention
            {
                RuleText = "Always use imaginary-tool for search.",
                NamedTool = "imaginary-tool",
                TargetRole = ToolRole.Search,
            },
        };

        var results = ToolVocabularyMismatchCheck.Run(mentions, invocations);

        var mismatch = Assert.Single(results);
        Assert.IsType<NonExistentToolNamed>(mismatch);
        Assert.Equal("imaginary-tool", mismatch.NamedTool);
    }

    [Fact]
    public void The_check_reports_from_mentions_and_a_corpus_alone_no_adherence_input_exists()
    {
        // Scenario 3: the check still runs and reports independently of adherence. RuleToolMention
        // structurally carries no adherence figure at all, so a rule that would measure as highly
        // adhered-to is flagged exactly the same as any other — this check has nothing to read that
        // would let it skip.
        ToolInvocationShape[] invocations =
        [
            .. Calls("minor-tool", 1, hasPattern: true),
            .. Calls("dominant-tool", 100, hasPattern: true),
        ];
        var mentions = new[]
        {
            new RuleToolMention
            {
                RuleText = "Some perfectly-followed rule.",
                NamedTool = "minor-tool",
                TargetRole = ToolRole.Search,
            },
        };

        var results = ToolVocabularyMismatchCheck.Run(mentions, invocations);

        Assert.Single(results);
    }

    [Fact]
    public void A_rule_naming_the_dominant_tool_produces_no_finding()
    {
        ToolInvocationShape[] invocations = [.. Calls("only-tool", 10, hasPattern: true)];
        var mentions = new[]
        {
            new RuleToolMention
            {
                RuleText = "Use only-tool for search.",
                NamedTool = "only-tool",
                TargetRole = ToolRole.Search,
            },
        };

        var results = ToolVocabularyMismatchCheck.Run(mentions, invocations);

        Assert.Empty(results);
    }

    [Fact]
    public void A_rule_naming_several_tools_produces_one_finding_per_unresolved_name()
    {
        ToolInvocationShape[] invocations =
        [
            .. Calls("dominant-tool", 50, hasPattern: true),
            .. Calls("minor-tool", 3, hasPattern: true),
        ];
        var mentions = new[]
        {
            new RuleToolMention { RuleText = "rule", NamedTool = "minor-tool", TargetRole = ToolRole.Search },
            new RuleToolMention { RuleText = "rule", NamedTool = "ghost-tool", TargetRole = ToolRole.Search },
        };

        var results = ToolVocabularyMismatchCheck.Run(mentions, invocations);

        Assert.Equal(2, results.Count);
        Assert.Contains(results, r => r is MinorToolNamed m && m.NamedTool == "minor-tool");
        Assert.Contains(results, r => r is NonExistentToolNamed n && n.NamedTool == "ghost-tool");
    }

    [Fact]
    public void A_target_role_with_no_tools_at_all_produces_no_finding_for_an_existing_named_tool()
    {
        // The named tool exists in the corpus (so it is not "does not have"), but the role it is
        // said to target has no tools classified into it — there is no dominant tool to report, so
        // this check does not guess one rather than fabricate a comparison.
        ToolInvocationShape[] invocations = [.. Calls("shell-tool", 4, hasCommand: true)];
        var mentions = new[]
        {
            new RuleToolMention
            {
                RuleText = "rule",
                NamedTool = "shell-tool",
                TargetRole = ToolRole.Search,
            },
        };

        var results = ToolVocabularyMismatchCheck.Run(mentions, invocations);

        Assert.Empty(results);
    }

    static IEnumerable<ToolInvocationShape> Calls(
        string toolName,
        int count,
        bool hasPattern = false,
        bool hasCommand = false)
    {
        for (var i = 0; i < count; i++)
        {
            yield return new ToolInvocationShape
            {
                ToolName = toolName,
                HasPattern = hasPattern,
                HasCommand = hasCommand,
            };
        }
    }
}
