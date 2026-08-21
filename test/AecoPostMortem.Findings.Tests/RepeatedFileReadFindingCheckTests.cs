using AecoPostMortem.Data.Execution;

namespace AecoPostMortem.Findings.Tests;

/// <summary>
/// FR-15 / issue #25, orchestration layer: reads <c>ToolCall</c> operands through
/// <c>AecoPostMortem.Data</c>, feeds <c>AecoPostMortem.Rules.RepeatedReadCheck</c> its generic
/// read events, and folds the result into a registered <see cref="Finding"/> per path (FR-57's
/// recurrence key for this class) plus a <see cref="CheckRegistryEntry"/> (S-44 Scenario 4: every
/// check is registered whether or not it fired).
/// </summary>
public sealed class RepeatedFileReadFindingCheckTests
{
    const string ReadToolName = "view";

    static ToolCall Read(string sessionId, string toolCallId, string path) => new()
    {
        SessionId = sessionId,
        ToolCallId = toolCallId,
        ToolName = ReadToolName,
        StartedAt = "2026-08-16T00:00:00Z",
        Path = path,
        OwnerKind = OwnerKind.Main,
    };

    /// <summary>Scenario 1 (issue #25): a session that opened one path four or more times produces
    /// a finding reporting that path with its read count for that session.</summary>
    [Fact]
    public void A_session_with_a_path_read_four_times_produces_a_finding_with_the_read_count()
    {
        ToolCall[] toolCalls =
        [
            Read("session-1", "tc-1", "src/Foo.cs"),
            Read("session-1", "tc-2", "src/Foo.cs"),
            Read("session-1", "tc-3", "src/Foo.cs"),
            Read("session-1", "tc-4", "src/Foo.cs"),
        ];

        var result = RepeatedFileReadFindingCheck.Run(toolCalls);

        var finding = Assert.Single(result.Findings);
        Assert.Equal(FindingClass.Waste, finding.Class);
        Assert.Equal("src/Foo.cs", finding.Recurrence.Key);
        Assert.Single(finding.Recurrence.Occurrences);
        Assert.Equal("session-1", finding.Recurrence.Occurrences[0].SessionId);
        Assert.Contains(
            finding.Evidence,
            item => item.Field == "read_count:session-1" && item.Value == "4");
    }

    /// <summary>Scenario 2 (issue #25): ranked in the digest, the finding states how many sessions
    /// it touched — one finding per path (the recurrence key), not one per session.</summary>
    [Fact]
    public void A_path_repeated_in_two_sessions_is_one_finding_whose_recurrence_states_both()
    {
        ToolCall[] toolCalls =
        [
            Read("session-1", "tc-1", "src/Foo.cs"),
            Read("session-1", "tc-2", "src/Foo.cs"),
            Read("session-1", "tc-3", "src/Foo.cs"),
            Read("session-1", "tc-4", "src/Foo.cs"),
            Read("session-2", "tc-5", "src/Foo.cs"),
            Read("session-2", "tc-6", "src/Foo.cs"),
            Read("session-2", "tc-7", "src/Foo.cs"),
            Read("session-2", "tc-8", "src/Foo.cs"),
            Read("session-2", "tc-9", "src/Foo.cs"),
        ];

        var result = RepeatedFileReadFindingCheck.Run(toolCalls);

        var finding = Assert.Single(result.Findings);
        Assert.Equal(2, finding.Recurrence.Occurrences.Count);
        Assert.Contains(finding.Recurrence.Occurrences, o => o.SessionId == "session-1");
        Assert.Contains(finding.Recurrence.Occurrences, o => o.SessionId == "session-2");
        Assert.Contains(
            finding.Evidence,
            item => item.Field == "read_count:session-1" && item.Value == "4");
        Assert.Contains(
            finding.Evidence,
            item => item.Field == "read_count:session-2" && item.Value == "5");
        Assert.Equal("src/Foo.cs was read 9 times across 2 sessions.", finding.Headline);
    }

    /// <summary>Scenario 3 (issue #25): a session where no path was read more than three times
    /// produces nothing for that session.</summary>
    [Fact]
    public void A_session_with_no_path_read_more_than_three_times_produces_no_finding()
    {
        ToolCall[] toolCalls =
        [
            Read("session-1", "tc-1", "src/Foo.cs"),
            Read("session-1", "tc-2", "src/Foo.cs"),
            Read("session-1", "tc-3", "src/Foo.cs"),
        ];

        var result = RepeatedFileReadFindingCheck.Run(toolCalls);

        Assert.Empty(result.Findings);
    }

    /// <summary>S-44 Scenario 4: this check is registered whether or not it fired — a clean run is
    /// a real <see cref="CheckRunStatus.Ran"/> with <see cref="CheckRegistryEntry.FindingCount"/> of
    /// zero, never absent from the registry.</summary>
    [Fact]
    public void A_clean_run_still_registers_with_zero_findings_not_refused()
    {
        ToolCall[] toolCalls =
        [
            Read("session-1", "tc-1", "src/Foo.cs"),
            Read("session-1", "tc-2", "src/Foo.cs"),
        ];

        var result = RepeatedFileReadFindingCheck.Run(toolCalls);

        Assert.Equal(CheckRunStatus.Ran, result.RegistryEntry.Status);
        Assert.Equal(0, result.RegistryEntry.FindingCount);
        Assert.Equal(1, result.RegistryEntry.Population);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void The_registry_entry_reports_the_finding_count_and_session_population()
    {
        ToolCall[] toolCalls =
        [
            Read("session-1", "tc-1", "src/Foo.cs"),
            Read("session-1", "tc-2", "src/Foo.cs"),
            Read("session-1", "tc-3", "src/Foo.cs"),
            Read("session-1", "tc-4", "src/Foo.cs"),
            Read("session-2", "tc-5", "src/Bar.cs"),
        ];

        var result = RepeatedFileReadFindingCheck.Run(toolCalls);

        Assert.Equal(1, result.RegistryEntry.FindingCount);
        Assert.Equal(2, result.RegistryEntry.Population);
        Assert.Equal(Provenance.Derived, result.RegistryEntry.Provenance);
    }

    /// <summary>The operand boundary: a tool call that carries a path but is not a read is not
    /// counted, even at four or more. The role/vocabulary derivation this filter stands in for is
    /// S-21's job, not this check's — see this file's own remarks.</summary>
    [Fact]
    public void A_non_read_tool_call_with_a_path_is_not_counted()
    {
        ToolCall[] toolCalls =
        [
            new()
            {
                SessionId = "session-1",
                ToolCallId = "tc-1",
                ToolName = "create_file",
                StartedAt = "2026-08-16T00:00:00Z",
                Path = "src/Foo.cs",
                OwnerKind = OwnerKind.Main,
            },
            new()
            {
                SessionId = "session-1",
                ToolCallId = "tc-2",
                ToolName = "create_file",
                StartedAt = "2026-08-16T00:00:00Z",
                Path = "src/Foo.cs",
                OwnerKind = OwnerKind.Main,
            },
            new()
            {
                SessionId = "session-1",
                ToolCallId = "tc-3",
                ToolName = "create_file",
                StartedAt = "2026-08-16T00:00:00Z",
                Path = "src/Foo.cs",
                OwnerKind = OwnerKind.Main,
            },
            new()
            {
                SessionId = "session-1",
                ToolCallId = "tc-4",
                ToolName = "create_file",
                StartedAt = "2026-08-16T00:00:00Z",
                Path = "src/Foo.cs",
                OwnerKind = OwnerKind.Main,
            },
        ];

        var result = RepeatedFileReadFindingCheck.Run(toolCalls);

        Assert.Empty(result.Findings);
    }

    /// <summary>A read tool call with no path (the parser-defect case named in the issue's edge
    /// cases) is excluded rather than crashing the check.</summary>
    [Fact]
    public void A_read_tool_call_with_no_path_is_excluded()
    {
        ToolCall[] toolCalls =
        [
            new()
            {
                SessionId = "session-1",
                ToolCallId = "tc-1",
                ToolName = ReadToolName,
                StartedAt = "2026-08-16T00:00:00Z",
                Path = null,
                OwnerKind = OwnerKind.Main,
            },
        ];

        var result = RepeatedFileReadFindingCheck.Run(toolCalls);

        Assert.Empty(result.Findings);
        Assert.Equal(1, result.RegistryEntry.Population);
    }
}
