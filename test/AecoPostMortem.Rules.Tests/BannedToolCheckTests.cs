namespace AecoPostMortem.Rules.Tests;

/// <summary>Piece 3's ToolIsBanned adherence check: a prohibition names one tool, and the only
/// adherence-worthy question is whether that tool was actually called — never a role comparison
/// (see the design note on why <see cref="ToolVocabularyMismatchCheck"/> does not fit a
/// prohibition).</summary>
public sealed class BannedToolCheckTests
{
    [Fact]
    public void A_banned_tool_that_was_called_is_reported_with_its_call_count()
    {
        ToolInvocationShape[] invocations = [.. Calls("grep", 3, hasPattern: true)];
        var mentions = new[]
        {
            new BannedToolMention { RuleText = "Never use grep.", NamedTool = "grep" },
        };

        var results = BannedToolCheck.Run(mentions, invocations);

        var usage = Assert.Single(results);
        Assert.Equal("grep", usage.NamedTool);
        Assert.Equal(3, usage.CallCount);
        Assert.Contains("grep", usage.ResolvedTools);
    }

    [Fact]
    public void A_banned_tool_that_was_never_called_is_unresolved_and_produces_no_result()
    {
        // OperandResolver can only ever resolve a name to tools that were actually observed calling
        // — exact-name, server-field and role-layer resolution are all derived from real calls — so a
        // banned tool that was never called is indistinguishable from an unknown name: both are
        // Unresolved, and neither is a violation worth reporting (the ban was, if anything, honored).
        ToolInvocationShape[] invocations = [.. Calls("grep", 3, hasPattern: true)];
        var mentions = new[]
        {
            new BannedToolMention { RuleText = "Never use rg.", NamedTool = "rg" },
        };

        var results = BannedToolCheck.Run(mentions, invocations);

        Assert.Empty(results);
    }

    [Fact]
    public void A_banned_tool_that_does_not_resolve_to_anything_in_the_corpus_produces_no_result()
    {
        ToolInvocationShape[] invocations = [.. Calls("grep", 3, hasPattern: true)];
        var mentions = new[]
        {
            new BannedToolMention { RuleText = "Never use imaginary-tool.", NamedTool = "imaginary-tool" },
        };

        var results = BannedToolCheck.Run(mentions, invocations);

        Assert.Empty(results);
    }

    [Fact]
    public void A_banned_tool_resolved_through_the_server_field_layer_sums_calls_across_every_resolved_tool()
    {
        ToolInvocationShape[] invocations =
        [
            .. Calls("mcp-tool-a", 2, mcpServerName: "banned-server"),
            .. Calls("mcp-tool-b", 4, mcpServerName: "banned-server"),
        ];
        var mentions = new[]
        {
            new BannedToolMention { RuleText = "Never use banned-server.", NamedTool = "banned-server" },
        };

        var results = BannedToolCheck.Run(mentions, invocations);

        var usage = Assert.Single(results);
        Assert.Equal(6, usage.CallCount);
        Assert.Equal(2, usage.ResolvedTools.Count);
    }

    static IEnumerable<ToolInvocationShape> Calls(
        string toolName,
        int count,
        bool hasPattern = false,
        string? mcpServerName = null)
    {
        for (var i = 0; i < count; i++)
        {
            yield return new ToolInvocationShape
            {
                ToolName = toolName,
                HasPattern = hasPattern,
                McpServerName = mcpServerName,
            };
        }
    }
}
