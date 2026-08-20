using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using AecoPostMortem.Findings;
using AecoPostMortem.Rules;

namespace AecoPostMortem.Api.Tests;

/// <summary>
/// FR-39's served comparison (S-35, issue #43): the wire shape has to keep the same guarantee the
/// domain type makes structural — a side's session count is on the same required member as its
/// percentage, never a field a client could drop while still rendering the number
/// (Scenario 2: "the session count on each side is as visible as the percentage").
/// </summary>
public sealed class MonitorComparisonEnvelopeTests
{
    static RuleSetVersion Version(string hash, string firstSessionId, int sessionCount) => new()
    {
        Id = new RuleSetVersionId { Repository = "supahfly27/UpFront", Hash = hash },
        FirstSessionId = firstSessionId,
        FirstSessionStartedAt = firstSessionId,
        LastSessionId = firstSessionId,
        SessionCount = sessionCount,
    };

    static ToolInvocationShape[] Calls(string toolName, int count) =>
        Enumerable.Repeat(new ToolInvocationShape { ToolName = toolName, HasPattern = true }, count).ToArray();

    static MonitorComparison SampleComparison()
    {
        RuleSetVersion[] versions = [Version("hash-1", "s1", 3), Version("hash-2", "s4", 4)];

        return MonitorComparison.Compare(
            versions,
            new RuleSetVersionId { Repository = "supahfly27/UpFront", Hash = "hash-1" },
            new RuleSetVersionId { Repository = "supahfly27/UpFront", Hash = "hash-2" },
            operandAText: "rg",
            operandBText: "grep",
            beforeInvocations: Calls("rg", 4).Concat(Calls("grep", 6)).ToArray(),
            afterInvocations: Calls("rg", 9).Concat(Calls("grep", 1)).ToArray());
    }

    [Fact]
    public void From_carries_each_sides_session_count_alongside_its_figure()
    {
        var envelope = MonitorComparisonEnvelope.From(SampleComparison());

        Assert.Equal(3, envelope.BeforeVersion.SessionCount);
        Assert.Equal(4, envelope.AfterVersion.SessionCount);
        Assert.Equal(40d, envelope.Before.Percentage);
        Assert.Equal(90d, envelope.After.Percentage);
    }

    [Fact]
    public void The_session_count_serialises_in_the_same_object_as_the_percentage()
    {
        var json = JsonSerializer.Serialize(MonitorComparisonEnvelope.From(SampleComparison()));

        Assert.Contains("\"SessionCount\"", json, StringComparison.Ordinal);
        Assert.Contains("\"Percentage\"", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(nameof(MonitorComparisonEnvelope.BeforeVersion))]
    [InlineData(nameof(MonitorComparisonEnvelope.AfterVersion))]
    [InlineData(nameof(MonitorComparisonEnvelope.Before))]
    [InlineData(nameof(MonitorComparisonEnvelope.After))]
    public void Every_member_is_required(string propertyName)
    {
        var property = typeof(MonitorComparisonEnvelope).GetProperty(propertyName);

        Assert.NotNull(property);
        Assert.NotNull(property!.GetCustomAttribute<RequiredMemberAttribute>());
    }
}
