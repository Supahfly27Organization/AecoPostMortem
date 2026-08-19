using System.Reflection;
using System.Runtime.CompilerServices;

namespace AecoPostMortem.Findings.Tests;

/// <summary>
/// Scenario 1 and Scenario 2 of the finding contract (issue #23): construction fails without a
/// provenance level, and the record carries everything the surfaces need — class, provenance,
/// evidence, recurrence, the resolution used where one applies, its suggestion, and the operator's
/// response. No other field.
/// </summary>
public sealed class FindingTests
{
    [Fact]
    public void Provenance_is_a_required_member()
    {
        var property = typeof(Finding).GetProperty(nameof(Finding.Provenance));

        Assert.NotNull(property);
        Assert.NotNull(property!.GetCustomAttribute<RequiredMemberAttribute>());
    }

    [Fact]
    public void The_record_carries_everything_the_surfaces_need()
    {
        var finding = new Finding
        {
            Class = FindingClass.RuleAdherenceToolChoice,
            Provenance = Provenance.Derived,
            Evidence = [new EvidenceItem { Field = "data.toolName", Value = "grep" }],
            Recurrence = new Recurrence
            {
                Key = "prefer rg over grep",
                Occurrences = [new RecurrenceOccurrence { SessionId = "session-1", RuleSetVersion = "v3" }],
            },
            Resolution = new Resolution { OperandLayer = "NORMALIZED", CallCount = 12 },
            Suggestion = new Suggestion { Text = "name `rg`" },
            OperatorResponse = OperatorResponse.Accepted,
        };

        Assert.Equal(FindingClass.RuleAdherenceToolChoice, finding.Class);
        Assert.Equal(Provenance.Derived, finding.Provenance);
        Assert.Single(finding.Evidence);
        Assert.Equal("prefer rg over grep", finding.Recurrence.Key);
        Assert.Equal(12, finding.Resolution!.CallCount);
        Assert.Equal("name `rg`", finding.Suggestion!.Text);
        Assert.Equal(OperatorResponse.Accepted, finding.OperatorResponse);
    }

    [Fact]
    public void Resolution_and_suggestion_are_optional_and_operator_response_defaults_to_ignored()
    {
        var finding = new Finding
        {
            Class = FindingClass.Waste,
            Provenance = Provenance.Derived,
            Evidence = [new EvidenceItem { Field = "data.path", Value = "src/foo.cs" }],
            Recurrence = new Recurrence
            {
                Key = "src/foo.cs",
                Occurrences = [new RecurrenceOccurrence { SessionId = "session-1" }],
            },
        };

        Assert.Null(finding.Resolution);
        Assert.Null(finding.Suggestion);
        Assert.Equal(OperatorResponse.Ignored, finding.OperatorResponse);
    }

    /// <summary>The edge case named in issue #23: a finding whose recurrence is one session still
    /// carries a recurrence value rather than omitting the field.</summary>
    [Fact]
    public void A_single_session_finding_still_carries_a_recurrence_value()
    {
        var finding = new Finding
        {
            Class = FindingClass.MissingCapability,
            Provenance = Provenance.Inferred,
            Evidence = [new EvidenceItem { Field = "data.toolName", Value = "web_fetch" }],
            Recurrence = new Recurrence
            {
                Key = "web_fetch",
                Occurrences = [new RecurrenceOccurrence { SessionId = "session-1" }],
            },
        };

        Assert.NotNull(finding.Recurrence);
        Assert.Single(finding.Recurrence.Occurrences);
    }

    [Fact]
    public void The_four_finding_classes_are_numbered_to_match_the_PRD_table()
    {
        Assert.Equal(1, (int)FindingClass.RuleAdherenceToolChoice);
        Assert.Equal(2, (int)FindingClass.Waste);
        Assert.Equal(3, (int)FindingClass.RuleAdherenceWrittenContent);
        Assert.Equal(4, (int)FindingClass.MissingCapability);
    }

    [Fact]
    public void The_three_provenance_levels_are_distinct()
    {
        Assert.Equal(3, Enum.GetValues<Provenance>().Length);
    }

    [Fact]
    public void The_three_operator_responses_are_distinct()
    {
        Assert.Equal(3, Enum.GetValues<OperatorResponse>().Length);
    }
}
