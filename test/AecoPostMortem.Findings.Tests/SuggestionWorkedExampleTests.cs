namespace AecoPostMortem.Findings.Tests;

/// <summary>
/// Scenario "A suggestion is a template bound to a check shape" (issue #48): the check-shape
/// catalogue in <c>AecoPostMortem.Rules</c> is still empty, so this test plays the part of the
/// orchestration code a later story writes — the code that takes a check's own result, turns its
/// operands into <see cref="EvidenceItem"/>s, and renders a <see cref="Suggestion"/> from the exact
/// same evidence and resolution the resulting <see cref="Finding"/> carries. It generalises FR-35's
/// worked example: a rule written as "prefer ripgrep and glob for search" fires against an agent
/// whose own vocabulary calls those tools <c>rg</c>, <c>glob</c> and <c>view</c>.
/// </summary>
public sealed class SuggestionWorkedExampleTests
{
    /// <summary>Stands in for a check shape's own result type from <c>AecoPostMortem.Rules</c> —
    /// plain data, no persistence, no tool name baked into the shape itself (only the orchestration
    /// layer that reads it, here, ever writes one down).</summary>
    sealed record ToolChoiceCheckResult(
        string RuleStatement,
        IReadOnlyList<string> AgentVocabularyToolNames,
        string OperandLayer,
        int CallCount);

    static readonly SuggestionTemplate ToolChoiceTemplate = new()
    {
        CheckId = "rule-adherence-tool-choice",
        Format = "rewrite the rule in your agent's own vocabulary: name {data.toolName}",
    };

    [Fact]
    public void A_findings_suggestion_is_populated_from_the_same_evidence_and_resolution_it_carries()
    {
        var checkResult = new ToolChoiceCheckResult(
            RuleStatement: "prefer ripgrep and glob for search",
            AgentVocabularyToolNames: ["rg", "glob", "view"],
            OperandLayer: "NORMALIZED",
            CallCount: 12);

        var evidence = checkResult.AgentVocabularyToolNames
            .Select(name => new EvidenceItem { Field = "data.toolName", Value = name })
            .ToArray();
        var resolution = new Resolution
        {
            OperandLayer = checkResult.OperandLayer,
            CallCount = checkResult.CallCount,
        };

        var finding = new Finding
        {
            Class = FindingClass.RuleAdherenceToolChoice,
            Provenance = Provenance.Derived,
            Headline = "the wrong tool vocabulary was used against the rule",
            Evidence = evidence,
            Recurrence = new Recurrence
            {
                Key = checkResult.RuleStatement,
                Occurrences = [new RecurrenceOccurrence { SessionId = "session-1" }],
            },
            Resolution = resolution,
            Suggestion = SuggestionRenderer.Render(ToolChoiceTemplate, evidence, resolution),
        };

        Assert.Equal(
            "rewrite the rule in your agent's own vocabulary: name `rg`, `glob` and `view`",
            finding.Suggestion!.Text);

        // The defining property of Scenario 1: re-rendering from the finding's own Evidence and
        // Resolution — not from the original check result — reproduces the same suggestion, because
        // those two fields are the only operands the template is allowed to see.
        var rerendered = SuggestionRenderer.Render(ToolChoiceTemplate, finding.Evidence, finding.Resolution);
        Assert.Equal(finding.Suggestion.Text, rerendered!.Text);
    }
}
