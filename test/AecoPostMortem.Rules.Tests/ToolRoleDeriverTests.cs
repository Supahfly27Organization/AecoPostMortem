namespace AecoPostMortem.Rules.Tests;

/// <summary>Scenarios 2, 3, 4 and 5 of issue #34: roles are derived from argument shapes alone,
/// never from a tool's name.</summary>
public sealed class ToolRoleDeriverTests
{
    [Fact]
    public void A_tool_taking_a_path_but_no_pattern_is_classified_as_file_reading()
    {
        ToolInvocationShape[] invocations = [new() { ToolName = "reader", HasPath = true }];

        var derivation = ToolRoleDeriver.Derive(invocations);

        Assert.Contains(
            derivation.Roles[ToolRole.FileRead].Tools,
            tool => tool.ToolName == "reader");
    }

    [Fact]
    public void A_tool_taking_a_pattern_is_classified_as_searching()
    {
        ToolInvocationShape[] invocations =
            [new() { ToolName = "searcher", HasPath = true, HasPattern = true }];

        var derivation = ToolRoleDeriver.Derive(invocations);

        Assert.Contains(
            derivation.Roles[ToolRole.Search].Tools,
            tool => tool.ToolName == "searcher");
    }

    [Fact]
    public void A_tool_taking_replacement_text_is_classified_as_writing()
    {
        ToolInvocationShape[] invocations =
            [new() { ToolName = "editor", HasPath = true, HasReplacement = true }];

        var derivation = ToolRoleDeriver.Derive(invocations);

        Assert.Contains(
            derivation.Roles[ToolRole.FileWrite].Tools,
            tool => tool.ToolName == "editor");
    }

    [Fact]
    public void A_tool_taking_file_text_is_classified_as_writing()
    {
        ToolInvocationShape[] invocations =
            [new() { ToolName = "writer", HasPath = true, HasFileText = true }];

        var derivation = ToolRoleDeriver.Derive(invocations);

        Assert.Contains(
            derivation.Roles[ToolRole.FileWrite].Tools,
            tool => tool.ToolName == "writer");
    }

    [Fact]
    public void A_tool_taking_a_command_is_classified_as_shell()
    {
        ToolInvocationShape[] invocations = [new() { ToolName = "runner", HasCommand = true }];

        var derivation = ToolRoleDeriver.Derive(invocations);

        Assert.Contains(
            derivation.Roles[ToolRole.Shell].Tools,
            tool => tool.ToolName == "runner");
    }

    [Fact]
    public void A_tool_that_spawns_an_agent_is_classified_as_spawn()
    {
        ToolInvocationShape[] invocations = [new() { ToolName = "delegator", SpawnsAgent = true }];

        var derivation = ToolRoleDeriver.Derive(invocations);

        Assert.Contains(
            derivation.Roles[ToolRole.Spawn].Tools,
            tool => tool.ToolName == "delegator");
    }

    [Fact]
    public void All_five_roles_are_populated_from_the_observed_tools()
    {
        ToolInvocationShape[] invocations =
        [
            new() { ToolName = "reader", HasPath = true },
            new() { ToolName = "searcher", HasPattern = true },
            new() { ToolName = "writer", HasFileText = true },
            new() { ToolName = "runner", HasCommand = true },
            new() { ToolName = "delegator", SpawnsAgent = true },
        ];

        var derivation = ToolRoleDeriver.Derive(invocations);

        Assert.NotEmpty(derivation.Roles[ToolRole.FileRead].Tools);
        Assert.NotEmpty(derivation.Roles[ToolRole.Search].Tools);
        Assert.NotEmpty(derivation.Roles[ToolRole.FileWrite].Tools);
        Assert.NotEmpty(derivation.Roles[ToolRole.Shell].Tools);
        Assert.NotEmpty(derivation.Roles[ToolRole.Spawn].Tools);
    }

    [Fact]
    public void Each_role_reports_its_dominant_tool_and_that_tools_call_count()
    {
        ToolInvocationShape[] invocations =
        [
            new() { ToolName = "minor-reader", HasPath = true },
            new() { ToolName = "major-reader", HasPath = true },
            new() { ToolName = "major-reader", HasPath = true },
            new() { ToolName = "major-reader", HasPath = true },
        ];

        var derivation = ToolRoleDeriver.Derive(invocations);
        var dominant = derivation.Roles[ToolRole.FileRead].DominantTool;

        Assert.NotNull(dominant);
        Assert.Equal("major-reader", dominant!.ToolName);
        Assert.Equal(3, dominant.CallCount);
    }

    [Fact]
    public void A_tool_whose_arguments_match_no_known_shape_is_recorded_as_unclassified()
    {
        ToolInvocationShape[] invocations = [new() { ToolName = "mystery" }];

        var derivation = ToolRoleDeriver.Derive(invocations);

        Assert.Contains("mystery", derivation.Unclassified);
        Assert.All(
            derivation.Roles.Values,
            role => Assert.DoesNotContain(role.Tools, tool => tool.ToolName == "mystery"));
    }

    [Fact]
    public void Derivation_is_computed_fresh_from_the_invocations_passed_in_not_cached()
    {
        ToolInvocationShape[] firstCorpus = [new() { ToolName = "alpha", HasPath = true }];
        ToolInvocationShape[] secondCorpus = [new() { ToolName = "beta", HasCommand = true }];

        var first = ToolRoleDeriver.Derive(firstCorpus);
        var second = ToolRoleDeriver.Derive(secondCorpus);

        Assert.Contains(first.Roles[ToolRole.FileRead].Tools, tool => tool.ToolName == "alpha");
        Assert.DoesNotContain(second.Roles[ToolRole.FileRead].Tools, tool => tool.ToolName == "alpha");
        Assert.Contains(second.Roles[ToolRole.Shell].Tools, tool => tool.ToolName == "beta");
    }
}
