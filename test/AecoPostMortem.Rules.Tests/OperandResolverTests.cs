namespace AecoPostMortem.Rules.Tests;

/// <summary>Issue #37 (S-23, FR-31/FR-32): a rule's operands are resolved in confidence order —
/// exact tool name, then the logged MCP server field, then the derived role, then unresolved —
/// and a two-operand resolution subtracts any tool both operands would otherwise claim, operand A
/// winning the tie.</summary>
public sealed class OperandResolverTests
{
    [Fact]
    public void An_exact_tool_name_resolves_at_the_first_layer()
    {
        ToolInvocationShape[] invocations = [new() { ToolName = "alpha-tool", HasPattern = true }];

        var resolved = OperandResolver.Resolve("alpha-tool", invocations);

        Assert.Equal(OperandResolutionLayer.ExactToolName, resolved.Layer);
        Assert.Equal(["alpha-tool"], resolved.Tools);
    }

    [Fact]
    public void The_mcp_server_field_resolves_when_no_exact_tool_name_matches()
    {
        ToolInvocationShape[] invocations =
        [
            new() { ToolName = "alpha-search", McpServerName = "alpha-server" },
            new() { ToolName = "alpha-write", McpServerName = "alpha-server" },
        ];

        var resolved = OperandResolver.Resolve("alpha-server", invocations);

        Assert.Equal(OperandResolutionLayer.McpServerField, resolved.Layer);
        Assert.Equal(
            new HashSet<string> { "alpha-search", "alpha-write" },
            resolved.Tools.ToHashSet());
    }

    [Fact]
    public void The_derived_role_resolves_when_neither_earlier_layer_matches()
    {
        ToolInvocationShape[] invocations =
        [
            new() { ToolName = "grep-like", HasPattern = true },
            new() { ToolName = "glob-like", HasPattern = true },
        ];

        var resolved = OperandResolver.Resolve(nameof(ToolRole.Search), invocations);

        Assert.Equal(OperandResolutionLayer.DerivedRole, resolved.Layer);
        Assert.Equal(
            new HashSet<string> { "grep-like", "glob-like" },
            resolved.Tools.ToHashSet());
    }

    [Fact]
    public void An_operand_matching_no_layer_is_reported_unresolved_not_dropped()
    {
        ToolInvocationShape[] invocations = [new() { ToolName = "alpha-tool", HasPattern = true }];

        var resolved = OperandResolver.Resolve("nothing-like-this-exists", invocations);

        Assert.Equal(OperandResolutionLayer.Unresolved, resolved.Layer);
        Assert.Empty(resolved.Tools);
    }

    [Fact]
    public void An_exact_tool_name_is_preferred_even_when_the_server_field_would_also_match()
    {
        ToolInvocationShape[] invocations =
        [
            new() { ToolName = "alpha-tool", HasPattern = true },
            new() { ToolName = "other-tool", McpServerName = "alpha-tool" },
        ];

        var resolved = OperandResolver.Resolve("alpha-tool", invocations);

        Assert.Equal(OperandResolutionLayer.ExactToolName, resolved.Layer);
        Assert.Equal(["alpha-tool"], resolved.Tools);
    }

    [Fact]
    public void The_server_field_is_a_structural_match_not_a_substring_match_on_tool_names()
    {
        // "alpha-server-search"'s own name contains "alpha-server" as a substring, but its own
        // logged McpServerName field is a different server — a naive substring match on ToolName
        // would wrongly pull it in (FR-31's edge case; issue #51's edge case documents the same
        // failure mode from the other direction).
        ToolInvocationShape[] invocations =
        [
            new() { ToolName = "search", McpServerName = "alpha-server" },
            new() { ToolName = "alpha-server-search", McpServerName = "beta-server" },
        ];

        var resolved = OperandResolver.Resolve("alpha-server", invocations);

        Assert.Equal(OperandResolutionLayer.McpServerField, resolved.Layer);
        Assert.Equal(["search"], resolved.Tools);
        Assert.DoesNotContain("alpha-server-search", resolved.Tools);
    }

    [Fact]
    public void A_tool_both_operands_could_claim_belongs_to_operand_a_only()
    {
        ToolInvocationShape[] invocations = [new() { ToolName = "shared-tool", HasPattern = true }];

        var resolution = OperandResolver.ResolveTwoOperands(
            "shared-tool",
            nameof(ToolRole.Search),
            invocations);

        Assert.Contains("shared-tool", resolution.OperandA.Tools);
        Assert.DoesNotContain("shared-tool", resolution.OperandB.Tools);
    }

    [Fact]
    public void Subtraction_leaves_operand_bs_own_uncontested_tools_untouched()
    {
        ToolInvocationShape[] invocations =
        [
            new() { ToolName = "shared-tool", HasPattern = true },
            new() { ToolName = "b-only-tool", HasPattern = true },
        ];

        var resolution = OperandResolver.ResolveTwoOperands(
            "shared-tool",
            nameof(ToolRole.Search),
            invocations);

        Assert.Equal(["shared-tool"], resolution.OperandA.Tools.ToHashSet());
        Assert.Equal(["b-only-tool"], resolution.OperandB.Tools.ToHashSet());
    }
}
