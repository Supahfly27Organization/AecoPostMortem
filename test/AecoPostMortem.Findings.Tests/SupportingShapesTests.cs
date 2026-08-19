namespace AecoPostMortem.Findings.Tests;

/// <summary>
/// The three leaf value types a <see cref="Finding"/> carries: a quoted event field (FR-59's
/// evidence), an adherence figure's resolution (FR-33), and a deterministic suggestion template
/// (FR-56). None depends on the others.
/// </summary>
public sealed class SupportingShapesTests
{
    [Fact]
    public void An_evidence_item_carries_the_field_and_the_quoted_value()
    {
        var evidence = new EvidenceItem { Field = "data.toolName", Value = "grep" };

        Assert.Equal("data.toolName", evidence.Field);
        Assert.Equal("grep", evidence.Value);
    }

    [Fact]
    public void A_resolution_carries_the_operand_layer_and_the_call_count()
    {
        var resolution = new Resolution { OperandLayer = "NORMALIZED", CallCount = 12 };

        Assert.Equal("NORMALIZED", resolution.OperandLayer);
        Assert.Equal(12, resolution.CallCount);
    }

    [Fact]
    public void A_suggestion_carries_its_rendered_text()
    {
        var suggestion = new Suggestion
        {
            Text = "rewrite the rule in your agent's own vocabulary: name `rg`",
        };

        Assert.Equal("rewrite the rule in your agent's own vocabulary: name `rg`", suggestion.Text);
    }
}
