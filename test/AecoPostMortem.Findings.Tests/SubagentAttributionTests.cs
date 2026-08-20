using AecoPostMortem.Data.Execution;
using AecoPostMortem.Rules;

namespace AecoPostMortem.Findings.Tests;

/// <summary>
/// FR-49 (S-43, issue #53): the product must never assert which rules a subagent ran under —
/// Copilot's system prompt carries no agent id, so a subagent's own rule set is genuinely
/// unrecoverable from the ingested store (this story's own edge case). Each test name maps to one
/// of the story's two Gherkin scenarios.
/// </summary>
public sealed class SubagentAttributionTests
{
    static Agent BuildAgent(string? description) => new()
    {
        SessionId = "s1",
        AgentId = "agent-1",
        SpawningToolCallId = "tc-task-1",
        Name = "reviewer",
        DisplayName = "Reviewer",
        Description = description,
        StartedAt = "2026-08-16T10:00:00Z",
        Outcome = AgentOutcome.Completed,
    };

    static Skill BuildSkill(string sessionId, string eventId, string name, OwnerKind ownerKind, string? agentId) => new()
    {
        SessionId = sessionId,
        EventId = eventId,
        Name = name,
        InvokedAt = "2026-08-16T10:01:00Z",
        OwnerKind = ownerKind,
        AgentId = agentId,
    };

    // --- Scenario 1: Inheritance is labelled, never asserted ---

    [Fact]
    public void The_default_rule_display_shows_nothing()
    {
        var display = SubagentRuleDisplay.Nothing;

        Assert.IsType<SubagentRuleDisplay.NoRuleSetShown>(display);
    }

    [Fact]
    public void An_explicit_inheritance_assumption_is_labelled_inferred()
    {
        var inherited = new[] { new RuleStatement { SourceFile = "CLAUDE.md", Text = "Always write tests first." } };

        var display = SubagentRuleDisplay.AssumedInherited(inherited);

        var assumption = Assert.IsType<SubagentRuleDisplay.InheritedRuleSetAssumption>(display);
        Assert.Equal(Provenance.Inferred, assumption.Provenance);
        Assert.Same(inherited, assumption.Rules);
    }

    [Fact]
    public void AssumedInherited_rejects_a_null_rule_list()
    {
        Assert.Throws<ArgumentNullException>(() => SubagentRuleDisplay.AssumedInherited(null!));
    }

    // --- Scenario 2: What is Observed is shown instead ---

    [Fact]
    public void A_subagents_spawn_description_task_prompt_and_own_skills_are_observed()
    {
        var agent = BuildAgent(description: "Review the diff for correctness bugs.");
        var skills = new[]
        {
            BuildSkill("s1", "e1", "code-review", OwnerKind.Agent, "agent-1"),
        };

        var context = SubagentObservedContext.From(agent, taskPrompt: "Review src/Foo.cs", sessionSkills: skills);

        Assert.Equal(Provenance.Observed, context.Provenance);
        Assert.Equal("Review the diff for correctness bugs.", context.SpawnDescription);
        Assert.Equal("Review src/Foo.cs", context.TaskPrompt);
        Assert.Equal(["code-review"], context.SkillInvocations);
    }

    /// <summary>A parent's or a sibling's skill invocation is never attributed to this agent.</summary>
    [Fact]
    public void Only_this_agents_own_skill_invocations_are_included()
    {
        var agent = BuildAgent(description: null);
        var skills = new[]
        {
            BuildSkill("s1", "e1", "main-thread-skill", OwnerKind.Main, agentId: null),
            BuildSkill("s1", "e2", "sibling-skill", OwnerKind.Agent, "agent-2"),
            BuildSkill("s1", "e3", "own-skill", OwnerKind.Agent, "agent-1"),
        };

        var context = SubagentObservedContext.From(agent, taskPrompt: null, sessionSkills: skills);

        Assert.Equal(["own-skill"], context.SkillInvocations);
    }

    [Fact]
    public void An_agent_with_no_description_no_task_prompt_and_no_skills_states_that_plainly()
    {
        var agent = BuildAgent(description: null);

        var context = SubagentObservedContext.From(agent, taskPrompt: null, sessionSkills: []);

        Assert.Null(context.SpawnDescription);
        Assert.Null(context.TaskPrompt);
        Assert.Empty(context.SkillInvocations);
    }

    [Fact]
    public void From_rejects_a_null_agent()
    {
        Assert.Throws<ArgumentNullException>(() => SubagentObservedContext.From(null!, taskPrompt: null, sessionSkills: []));
    }

    [Fact]
    public void From_rejects_a_null_skill_list()
    {
        Assert.Throws<ArgumentNullException>(() => SubagentObservedContext.From(BuildAgent(null), taskPrompt: null, sessionSkills: null!));
    }
}
