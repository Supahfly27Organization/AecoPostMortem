using AecoPostMortem.Data.Execution;

namespace AecoPostMortem.Findings.Tests;

/// <summary>
/// FR-20's orchestration (issue #30): permission prompts (<c>AecoPostMortem.Data.Execution.
/// Permission</c>) and questions put to the operator (a completed <c>ask_user</c> tool call) turned
/// into one <see cref="FindingClass.Waste"/> finding whose two counts never collapse into a summed
/// figure, and whose per-outcome evidence quotes <c>ResultKind</c> verbatim rather than inferring
/// denial from anything else.
/// </summary>
public sealed class InterruptionLoadFindingTests
{
    [Fact]
    public void Permission_prompts_and_questions_are_reported_as_distinct_counts_not_summed()
    {
        var permissions = new[]
        {
            Permission("s1", "e1", "approved"),
            Permission("s1", "e2", "approved"),
        };
        var toolCalls = new[] { AskUser("s1", "t1") };

        var result = InterruptionLoadFinding.Run(permissions, toolCalls);

        var finding = Assert.Single(result.Findings);
        Assert.Contains(finding.Evidence, item => item.Field == "permissionPromptCount" && item.Value == "2");
        Assert.Contains(finding.Evidence, item => item.Field == "questionCount" && item.Value == "1");
        // Nothing renders 2 + 1 = 3 as a combined "interruptions" figure.
        Assert.DoesNotContain(finding.Evidence, item => item.Value == "3");
        Assert.DoesNotContain(finding.Suggestion!.Text, "3 interruptions");
        Assert.Equal("2 permission prompts and 1 question interrupted the operator across 1 session.", finding.Headline);
    }

    [Fact]
    public void A_completed_permission_requests_outcome_is_read_from_the_result_kind_and_marked_observed()
    {
        var permissions = new[] { Permission("s1", "e1", "denied") };

        var result = InterruptionLoadFinding.Run(permissions, []);

        var finding = Assert.Single(result.Findings);
        Assert.Equal(Provenance.Observed, finding.Provenance);
        Assert.Contains(finding.Evidence, item => item.Field == "result_kind:denied" && item.Value == "1");
    }

    /// <summary>The edge case named in issue #30: a measured 1,033 permission prompts against a
    /// measured 1,031 completions means two prompts have no recorded outcome, which must render as
    /// "no outcome recorded" rather than as a denial.</summary>
    [Fact]
    public void An_unresolved_permission_prompt_renders_as_no_outcome_recorded_not_a_denial()
    {
        var permissions = new[]
        {
            Permission("s1", "e1", "approved"),
            PermissionWithNoOutcome("s1", "e2"),
        };

        var result = InterruptionLoadFinding.Run(permissions, []);

        var finding = Assert.Single(result.Findings);
        Assert.Contains(finding.Evidence, item => item.Field == "result_kind:no outcome recorded" && item.Value == "1");
        Assert.Contains(finding.Evidence, item => item.Field == "result_kind:approved" && item.Value == "1");
        Assert.Contains(finding.Evidence, item => item.Field == "permissionPromptCount" && item.Value == "2");
    }

    [Fact]
    public void The_recurrence_key_is_the_interruption_load_identity()
    {
        var permissions = new[] { Permission("s1", "e1", "approved") };

        var result = InterruptionLoadFinding.Run(permissions, []);

        var finding = Assert.Single(result.Findings);
        Assert.Equal("interruption-load", finding.Recurrence.Key);
        Assert.Contains(finding.Recurrence.Occurrences, occurrence => occurrence.SessionId == "s1");
    }

    [Fact]
    public void Non_ask_user_tool_calls_are_not_counted_as_questions()
    {
        var toolCalls = new[] { AskUser("s1", "t1"), ToolCall("s1", "t2", "view") };

        var result = InterruptionLoadFinding.Run([], toolCalls);

        var finding = Assert.Single(result.Findings);
        Assert.Contains(finding.Evidence, item => item.Field == "questionCount" && item.Value == "1");
    }

    [Fact]
    public void No_permission_prompts_and_no_questions_produces_no_findings_and_a_clean_registry_entry()
    {
        var result = InterruptionLoadFinding.Run([], []);

        Assert.Empty(result.Findings);
        Assert.Equal(CheckRunStatus.Ran, result.RegistryEntry.Status);
        Assert.Equal(0, result.RegistryEntry.Population);
        Assert.Equal(0, result.RegistryEntry.FindingCount);
    }

    [Fact]
    public void The_check_registers_with_its_population_and_finding_count()
    {
        var permissions = new[] { Permission("s1", "e1", "approved") };
        var toolCalls = new[] { AskUser("s2", "t1") };

        var result = InterruptionLoadFinding.Run(permissions, toolCalls);

        Assert.Equal(InterruptionLoadFinding.CheckId, result.RegistryEntry.CheckId);
        Assert.Equal(CheckRunStatus.Ran, result.RegistryEntry.Status);
        Assert.Equal(2, result.RegistryEntry.Population);
        Assert.Equal(1, result.RegistryEntry.FindingCount);
        Assert.Equal(Provenance.Observed, result.RegistryEntry.Provenance);
    }

    [Fact]
    public void The_finding_carries_no_resolution_because_it_is_not_an_adherence_figure()
    {
        var permissions = new[] { Permission("s1", "e1", "approved") };

        var result = InterruptionLoadFinding.Run(permissions, []);

        var finding = Assert.Single(result.Findings);
        Assert.Null(finding.Resolution);
    }

    static Permission Permission(string sessionId, string eventId, string resultKind) => new()
    {
        SessionId = sessionId,
        EventId = eventId,
        RequestedAt = "2026-08-20T00:00:00Z",
        CompletedAt = "2026-08-20T00:00:01Z",
        ResultKind = resultKind,
        OwnerKind = OwnerKind.Main,
    };

    static Permission PermissionWithNoOutcome(string sessionId, string eventId) => new()
    {
        SessionId = sessionId,
        EventId = eventId,
        RequestedAt = "2026-08-20T00:00:00Z",
        CompletedAt = null,
        ResultKind = null,
        OwnerKind = OwnerKind.Main,
    };

    static ToolCall AskUser(string sessionId, string toolCallId) => ToolCall(sessionId, toolCallId, "ask_user");

    static ToolCall ToolCall(string sessionId, string toolCallId, string toolName) => new()
    {
        SessionId = sessionId,
        ToolCallId = toolCallId,
        ToolName = toolName,
        StartedAt = "2026-08-20T00:00:00Z",
        CompletedAt = "2026-08-20T00:00:01Z",
        Success = true,
        OwnerKind = OwnerKind.Main,
    };
}
