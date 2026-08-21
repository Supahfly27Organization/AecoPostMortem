using AecoPostMortem.Data.Execution;
using AecoPostMortem.Findings;

namespace AecoPostMortem.Api.Tests;

/// <summary>
/// Mockup parity item #17: <see cref="SessionTapeStepFindingLookup"/> attaches a finding to the
/// specific tape step(s) it is unambiguously about, for the two finding shapes this project covers —
/// a tool-failure rate (<c>toolIdentity</c> evidence) and a hook failure (<c>data.success</c>/
/// <c>data.error</c> evidence). See `AecoPostMortem.Api/CLAUDE.md` for the full scoping reasoning.
/// </summary>
public sealed class SessionTapeStepFindingLookupTests
{
    static ToolCall Call(string id, string toolName, bool? success, string? mcpServerName = null) => new()
    {
        SessionId = "s1",
        ToolCallId = id,
        ToolName = toolName,
        StartedAt = "2026-08-16T10:00:00Z",
        Success = success,
        McpServerName = mcpServerName,
        OwnerKind = OwnerKind.Main,
    };

    static Hook HookRow(string eventId, string name, bool? success) => new()
    {
        SessionId = "s1",
        EventId = eventId,
        Name = name,
        StartedAt = "2026-08-16T10:00:00Z",
        Success = success,
        OwnerKind = OwnerKind.Main,
    };

    /// <summary>The exact evidence shape <c>FailedToolCallsFinding</c>/<c>ToolFailureClusterFinding</c>
    /// both build.</summary>
    static Finding ToolFailureFinding(string toolIdentity) => new()
    {
        Class = FindingClass.Waste,
        Provenance = Provenance.Derived,
        Headline = "irrelevant to this test",
        Evidence =
        [
            new EvidenceItem { Field = "toolIdentity", Value = toolIdentity },
            new EvidenceItem { Field = "failures", Value = "1" },
            new EvidenceItem { Field = "calls", Value = "2" },
        ],
        Recurrence = new Recurrence { Key = toolIdentity, Occurrences = [] },
    };

    /// <summary>The exact evidence shape <c>HookFailureFinding</c> builds.</summary>
    static Finding HookFinding(string hookName) => new()
    {
        Class = FindingClass.Waste,
        Provenance = Provenance.Observed,
        Headline = "irrelevant to this test",
        Evidence =
        [
            new EvidenceItem { Field = "data.success", Value = "false" },
            new EvidenceItem { Field = "data.error", Value = "boom" },
        ],
        Recurrence = new Recurrence { Key = hookName, Occurrences = [] },
    };

    [Fact]
    public void A_failed_tool_call_matching_the_findings_own_tool_identity_is_flagged()
    {
        var finding = ToolFailureFinding("view");
        var calls = new[] { Call("tc1", "view", success: false) };

        var result = SessionTapeStepFindingLookup.Build([finding], calls, []);

        var matches = Assert.Single(result[(SessionTapeStepKind.ToolCall, "tc1")]);
        Assert.Same(finding, matches);
    }

    /// <summary>The conservative reading this type documents: the finding's evidence is an aggregate
    /// rate, so every failed call of that identity is unambiguously part of what produced it — not
    /// only "the first" or "the most recent" one.</summary>
    [Fact]
    public void Every_failed_call_of_the_matching_tool_identity_is_flagged_not_only_the_first()
    {
        var finding = ToolFailureFinding("view");
        var calls = new[]
        {
            Call("tc1", "view", success: false),
            Call("tc2", "view", success: false),
            Call("tc3", "view", success: true),
        };

        var result = SessionTapeStepFindingLookup.Build([finding], calls, []);

        Assert.True(result.ContainsKey((SessionTapeStepKind.ToolCall, "tc1")));
        Assert.True(result.ContainsKey((SessionTapeStepKind.ToolCall, "tc2")));
        Assert.False(result.ContainsKey((SessionTapeStepKind.ToolCall, "tc3")));
    }

    [Fact]
    public void A_successful_call_of_the_same_tool_identity_is_never_flagged()
    {
        var finding = ToolFailureFinding("view");
        var calls = new[] { Call("tc1", "view", success: true) };

        var result = SessionTapeStepFindingLookup.Build([finding], calls, []);

        Assert.Empty(result);
    }

    [Fact]
    public void A_failed_call_of_a_different_tool_is_never_flagged()
    {
        var finding = ToolFailureFinding("view");
        var calls = new[] { Call("tc1", "grep", success: false) };

        var result = SessionTapeStepFindingLookup.Build([finding], calls, []);

        Assert.Empty(result);
    }

    [Fact]
    public void A_failed_call_naming_an_mcp_server_is_flagged_as_an_mcp_call_step_not_a_plain_tool_call()
    {
        var finding = ToolFailureFinding("search_graph");
        var calls = new[] { Call("tc1", "search_graph", success: false, mcpServerName: "codebase-memory") };

        var result = SessionTapeStepFindingLookup.Build([finding], calls, []);

        Assert.True(result.ContainsKey((SessionTapeStepKind.McpCall, "tc1")));
        Assert.False(result.ContainsKey((SessionTapeStepKind.ToolCall, "tc1")));
    }

    [Fact]
    public void A_failed_hook_matching_the_findings_own_hook_name_is_flagged()
    {
        var finding = HookFinding("sessionStart");
        var hooks = new[] { HookRow("h1", "sessionStart", success: false) };

        var result = SessionTapeStepFindingLookup.Build([finding], [], hooks);

        var matches = Assert.Single(result[(SessionTapeStepKind.Hook, "h1")]);
        Assert.Same(finding, matches);
    }

    [Fact]
    public void A_successful_hook_of_the_same_name_is_never_flagged()
    {
        var finding = HookFinding("sessionStart");
        var hooks = new[] { HookRow("h1", "sessionStart", success: true) };

        var result = SessionTapeStepFindingLookup.Build([finding], [], hooks);

        Assert.Empty(result);
    }

    [Fact]
    public void A_failed_hook_of_a_different_name_is_never_flagged()
    {
        var finding = HookFinding("sessionStart");
        var hooks = new[] { HookRow("h1", "postToolUse", success: false) };

        var result = SessionTapeStepFindingLookup.Build([finding], [], hooks);

        Assert.Empty(result);
    }

    /// <summary>A finding this type does not attempt to cover (e.g. a whole-session aggregate like
    /// <c>PhaseChurnFinding</c>) carries neither marker evidence shape, so it is attached to nothing —
    /// the honest "not attempted" state, never a guess.</summary>
    [Fact]
    public void A_finding_with_neither_covered_evidence_shape_is_attached_to_no_step()
    {
        var finding = new Finding
        {
            Class = FindingClass.Waste,
            Provenance = Provenance.Derived,
            Headline = "irrelevant to this test",
            Evidence = [new EvidenceItem { Field = "sessionId", Value = "s1" }],
            Recurrence = new Recurrence { Key = "s1", Occurrences = [] },
        };
        var calls = new[] { Call("tc1", "view", success: false) };
        var hooks = new[] { HookRow("h1", "sessionStart", success: false) };

        var result = SessionTapeStepFindingLookup.Build([finding], calls, hooks);

        Assert.Empty(result);
    }
}
