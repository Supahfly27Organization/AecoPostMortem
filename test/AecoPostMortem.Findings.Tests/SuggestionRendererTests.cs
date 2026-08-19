namespace AecoPostMortem.Findings.Tests;

/// <summary>
/// FR-56 (issue #48): a suggestion is a deterministic template bound to a check shape, populated
/// from the same evidence and resolution the finding used. No check shape exists yet in
/// <c>AecoPostMortem.Rules</c> — these tests construct a synthetic check shape (a rule-adherence
/// check that finds tool names the agent's own vocabulary already has) to exercise the mechanism
/// honestly, generalising FR-35's worked example: "name `rg`, `glob` and `view`".
/// </summary>
public sealed class SuggestionRendererTests
{
    [Fact]
    public void No_template_means_no_suggestion()
    {
        var evidence = new[] { new EvidenceItem { Field = "data.toolName", Value = "grep" } };

        var suggestion = SuggestionRenderer.Render(template: null, evidence, resolution: null);

        Assert.Null(suggestion);
    }

    [Fact]
    public void A_placeholder_is_populated_from_the_matching_evidence_field()
    {
        var template = new SuggestionTemplate
        {
            CheckId = "rule-adherence-tool-choice",
            Format = "rewrite the rule in your agent's own vocabulary: name {data.toolName}",
        };
        var evidence = new[] { new EvidenceItem { Field = "data.toolName", Value = "rg" } };

        var suggestion = SuggestionRenderer.Render(template, evidence, resolution: null);

        Assert.Equal(
            "rewrite the rule in your agent's own vocabulary: name `rg`",
            suggestion!.Text);
    }

    /// <summary>The worked example named in FR-56 and issue #48's edge cases, generalised: a rule
    /// that names tools the agent doesn't have gets rewritten in the agent's own vocabulary, and
    /// several evidence items sharing one field render as a joined list — "name `rg`, `glob` and
    /// `view`" — rather than only ever naming one tool.</summary>
    [Fact]
    public void Several_evidence_items_for_one_field_render_as_a_joined_list()
    {
        var template = new SuggestionTemplate
        {
            CheckId = "rule-adherence-tool-choice",
            Format = "rewrite the rule in your agent's own vocabulary: name {data.toolName}",
        };
        var evidence = new[]
        {
            new EvidenceItem { Field = "data.toolName", Value = "rg" },
            new EvidenceItem { Field = "data.toolName", Value = "glob" },
            new EvidenceItem { Field = "data.toolName", Value = "view" },
        };

        var suggestion = SuggestionRenderer.Render(template, evidence, resolution: null);

        Assert.Equal(
            "rewrite the rule in your agent's own vocabulary: name `rg`, `glob` and `view`",
            suggestion!.Text);
    }

    [Fact]
    public void A_placeholder_may_name_the_resolution_that_produced_the_finding()
    {
        var template = new SuggestionTemplate
        {
            CheckId = "rule-adherence-tool-choice",
            Format = "measured over {CallCount} calls resolved from {OperandLayer}",
        };
        var resolution = new Resolution { OperandLayer = "NORMALIZED", CallCount = 12 };

        var suggestion = SuggestionRenderer.Render(template, evidence: [], resolution);

        Assert.Equal("measured over 12 calls resolved from NORMALIZED", suggestion!.Text);
    }

    /// <summary>FR-56's edge case: a template that cannot name a concrete operand produces no
    /// suggestion, never a partially-filled or generic one — because §5.4 measures the rejection
    /// rate and a vague suggestion poisons that signal.</summary>
    [Fact]
    public void A_placeholder_with_no_matching_operand_yields_no_suggestion_rather_than_a_vague_one()
    {
        var template = new SuggestionTemplate
        {
            CheckId = "rule-adherence-tool-choice",
            Format = "name {data.toolName}",
        };

        var suggestion = SuggestionRenderer.Render(template, evidence: [], resolution: null);

        Assert.Null(suggestion);
    }

    /// <summary>A resolution placeholder with no resolution behaves the same way as any other
    /// unresolved operand: no suggestion, not a suggestion missing half its sentence.</summary>
    [Fact]
    public void A_resolution_placeholder_with_no_resolution_yields_no_suggestion()
    {
        var template = new SuggestionTemplate
        {
            CheckId = "rule-adherence-tool-choice",
            Format = "measured over {CallCount} calls",
        };

        var suggestion = SuggestionRenderer.Render(template, evidence: [], resolution: null);

        Assert.Null(suggestion);
    }

    /// <summary>Scenario "The same finding always yields the same suggestion": rendering the same
    /// template against the same evidence and resolution twice is byte-for-byte identical, because
    /// the mechanism is a pure function of its three inputs with nothing else to vary.</summary>
    [Fact]
    public void Rendering_the_same_inputs_twice_yields_identical_text()
    {
        var template = new SuggestionTemplate
        {
            CheckId = "rule-adherence-tool-choice",
            Format = "name {data.toolName}, measured over {CallCount} calls",
        };
        var evidence = new[]
        {
            new EvidenceItem { Field = "data.toolName", Value = "rg" },
            new EvidenceItem { Field = "data.toolName", Value = "glob" },
        };
        var resolution = new Resolution { OperandLayer = "NORMALIZED", CallCount = 12 };

        var first = SuggestionRenderer.Render(template, evidence, resolution);
        var second = SuggestionRenderer.Render(template, evidence, resolution);

        Assert.Equal(first!.Text, second!.Text);
    }
}
