using AecoPostMortem.Data.Execution;
using AecoPostMortem.Rules;

namespace AecoPostMortem.Findings;

/// <summary>
/// FR-49 (S-43, issue #53): what this product can honestly display for one subagent. Copilot's
/// system prompt carries no agent id — the story's own edge case — so there is no event this
/// product could quote as "the rules this subagent ran under": a subagent's own rule set is
/// genuinely unrecoverable from the ingested store, unlike a session's own (<see
/// cref="RuleStatementExtractor"/>, S-19). <see cref="SubagentRuleDisplay"/> is therefore closed to
/// exactly two shapes — <see cref="SubagentRuleDisplay.Nothing"/> (the default, and this story's own
/// preferred outcome — showing nothing beats a labelled guess) or an explicit <see
/// cref="SubagentRuleDisplay.AssumedInherited"/> a caller states on purpose. Nothing in this type
/// tries to infer or derive an inheritance assumption from context; a caller either supplies one
/// explicitly or gets <see cref="SubagentRuleDisplay.Nothing"/>.
/// </summary>
public abstract record SubagentRuleDisplay
{
    private SubagentRuleDisplay()
    {
    }

    /// <summary>The default, and this story's own preferred outcome (Scenario 1's edge case):
    /// no inheritance assumption is being made, so nothing is shown for this subagent's rules —
    /// never a labelled guess.</summary>
    public static SubagentRuleDisplay Nothing { get; } = new NoRuleSetShown();

    /// <summary>
    /// An explicit inheritance assumption a caller states on purpose — e.g. "assume this subagent
    /// inherited its spawning session's own rule set" — never derived or guessed by this type.
    /// <see cref="InheritedRuleSetAssumption.Provenance"/> is a computed property fixed to
    /// <see cref="Findings.Provenance.Inferred"/>, not a settable field, so an inherited rule set
    /// can never be labelled anything else (Scenario 1: "any inherited rule set is labelled
    /// Inferred").
    /// </summary>
    public static SubagentRuleDisplay AssumedInherited(IReadOnlyList<RuleStatement> inheritedFrom)
    {
        ArgumentNullException.ThrowIfNull(inheritedFrom);
        return new InheritedRuleSetAssumption { Rules = inheritedFrom };
    }

    /// <summary>Scenario 1's preferred outcome: no rules are shown for this subagent.</summary>
    public sealed record NoRuleSetShown : SubagentRuleDisplay;

    /// <summary>An explicit, caller-stated inheritance assumption, always Inferred.</summary>
    public sealed record InheritedRuleSetAssumption : SubagentRuleDisplay
    {
        public required IReadOnlyList<RuleStatement> Rules { get; init; }

        /// <summary>Computed, never settable — the one value this shape can carry (Scenario 1).</summary>
        public Provenance Provenance => Provenance.Inferred;
    }
}

/// <summary>
/// FR-49, Scenario 2: what genuinely was recorded for one subagent — never inherited, never
/// assumed, always <see cref="Findings.Provenance.Observed"/>. Built from <see
/// cref="Data.Execution.Agent"/> (its own <c>Description</c>, i.e. <c>subagent.started.data.
/// agentDescription</c> — S-49) and the <see cref="Skill"/> rows this agent itself owns (<see
/// cref="OwnerKind.Agent"/> and <c>AgentId</c> matching this agent's own — never a parent's or a
/// sibling's). <see cref="TaskPrompt"/> is a plain input rather than read off <c>Data</c> directly:
/// no derived entity yet carries the spawning <c>task</c> call's own prompt argument (<c>ToolCall</c>
/// has no <c>Arguments</c> column — only <c>Path</c> is extracted today, see
/// <c>AecoPostMortem.Ingestion/CLAUDE.md</c>, "arguments is parsed polymorphically") — the same
/// not-yet-wired gap <c>PhaseChurnFinding</c> documents for its own <c>DeclaredIntent</c> input.
/// </summary>
public sealed record SubagentObservedContext
{
    public required string SessionId { get; init; }

    public required string AgentId { get; init; }

    /// <summary><c>Agent.Description</c> — null when Copilot recorded none for this spawn.</summary>
    public string? SpawnDescription { get; init; }

    /// <summary>The spawning <c>task</c> call's own prompt argument. A plain, caller-supplied
    /// input — see this type's own remarks for why.</summary>
    public string? TaskPrompt { get; init; }

    /// <summary>This agent's own <c>skill.invoked</c> events, by name — never a parent's or a
    /// sibling's.</summary>
    public required IReadOnlyList<string> SkillInvocations { get; init; }

    /// <summary>Computed, never settable — every field on this shape is Observed by construction.</summary>
    public Provenance Provenance => Provenance.Observed;

    /// <summary>
    /// Builds this subagent's Observed context from its own <see cref="Agent"/> row and the whole
    /// session's <see cref="Skill"/> rows, filtered down to the ones this agent itself owns.
    /// </summary>
    public static SubagentObservedContext From(Agent agent, string? taskPrompt, IReadOnlyList<Skill> sessionSkills)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(sessionSkills);

        var ownSkills = sessionSkills
            .Where(skill => skill.OwnerKind == OwnerKind.Agent
                && string.Equals(skill.AgentId, agent.AgentId, StringComparison.Ordinal))
            .Select(skill => skill.Name)
            .ToArray();

        return new SubagentObservedContext
        {
            SessionId = agent.SessionId,
            AgentId = agent.AgentId,
            SpawnDescription = agent.Description,
            TaskPrompt = taskPrompt,
            SkillInvocations = ownSkills,
        };
    }
}
