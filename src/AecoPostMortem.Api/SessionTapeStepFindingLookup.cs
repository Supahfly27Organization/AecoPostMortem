using AecoPostMortem.Data.Execution;
using AecoPostMortem.Findings;

namespace AecoPostMortem.Api;

/// <summary>
/// Mockup parity item #17: attaches a finding to the specific tape step(s) it is unambiguously
/// *about*, for the narrow set of finding shapes whose own <see cref="Finding.Evidence"/> names an
/// identity (a tool name, a hook name) this session's own <see cref="ToolCall"/>/<see cref="Hook"/>
/// rows can be matched against exactly — never a guess at "the" step among several plausible ones.
/// See `AecoPostMortem.Api/CLAUDE.md`'s own remarks on this type for the full scoping reasoning and
/// which finding-producing checks were deliberately left uncovered.
///
/// Matching is by the marker <see cref="EvidenceItem.Field"/> name(s) each covered check's own
/// orchestrator already writes — not a new mechanism: <c>RulesInventoryEnvelope.cs</c>'s own
/// <c>BuildViolationCounts</c> already reads a specific check's evidence field by name (<c>call_count</c>,
/// <c>access_count</c>, ...) to join a served figure back to its source; this is the identical
/// technique applied to a new question ("which step", not "which count").
/// </summary>
public static class SessionTapeStepFindingLookup
{
    /// <summary><see cref="FailedToolCallsFinding"/> and <see cref="ToolFailureClusterFinding"/> both
    /// carry the exact tool identity their rate was computed over on this field
    /// (<see cref="ToolCallOutcome.ToolIdentity"/> verbatim, <c>ApiHost.ToToolCallOutcomes</c>) — the
    /// same value <see cref="ToolCall.ToolName"/> carries, ordinal-equal, no other resolution
    /// involved.</summary>
    const string ToolIdentityField = "toolIdentity";

    /// <summary><see cref="HookFailureFinding"/>'s own two evidence fields — present on no other
    /// check's <see cref="Finding.Evidence"/> in this project, so their joint presence alone
    /// identifies the finding shape without a second discriminator.</summary>
    const string HookSuccessField = "data.success";
    const string HookErrorField = "data.error";

    /// <summary>
    /// Builds the step-to-findings map for one session. <paramref name="sessionFindings"/> is already
    /// scoped to this session (<c>Findings.SessionFindings.For</c>'s own join on
    /// <c>Recurrence.Occurrences</c>) — this method does no further session filtering, only step
    /// matching within it. <paramref name="toolCalls"/>/<paramref name="hooks"/> are this session's
    /// own rows, the same ones <c>ApiHost.GetSession</c> already reads to build the tape itself.
    /// </summary>
    public static IReadOnlyDictionary<(SessionTapeStepKind Kind, string StepId), IReadOnlyList<Finding>> Build(
        IReadOnlyList<Finding> sessionFindings,
        IReadOnlyList<ToolCall> toolCalls,
        IReadOnlyList<Hook> hooks)
    {
        ArgumentNullException.ThrowIfNull(sessionFindings);
        ArgumentNullException.ThrowIfNull(toolCalls);
        ArgumentNullException.ThrowIfNull(hooks);

        var flags = new Dictionary<(SessionTapeStepKind, string), List<Finding>>();

        void Add(SessionTapeStepKind kind, string stepId, Finding finding)
        {
            var key = (kind, stepId);
            if (!flags.TryGetValue(key, out var list))
            {
                list = [];
                flags[key] = list;
            }

            list.Add(finding);
        }

        foreach (var finding in sessionFindings)
        {
            var toolIdentity = FindEvidence(finding, ToolIdentityField);
            if (toolIdentity is not null)
            {
                // Conservative reading of "attach to the step this finding is about": the finding's
                // own evidence is an aggregate rate over every failed call of this tool identity, not
                // one call singled out, so every failed call of that identity in this session is
                // unambiguously part of what produced the rate — attaching to all of them (rather than
                // guessing "the first" or "the most recent") is the reading that adds no information
                // the evidence does not already carry. See `Api/CLAUDE.md` for the fuller reasoning.
                foreach (var call in toolCalls)
                {
                    if (call.Success == false && string.Equals(call.ToolName, toolIdentity, StringComparison.Ordinal))
                    {
                        var kind = call.McpServerName is not null
                            ? SessionTapeStepKind.McpCall
                            : SessionTapeStepKind.ToolCall;
                        Add(kind, call.ToolCallId, finding);
                    }
                }

                continue;
            }

            if (HasHookFailureEvidence(finding))
            {
                // HookFailureFinding.cs groups by hook name and sets Recurrence.Key to it verbatim —
                // the same identity Hook.Name carries.
                var hookName = finding.Recurrence.Key;
                foreach (var hook in hooks)
                {
                    if (hook.Success == false && string.Equals(hook.Name, hookName, StringComparison.Ordinal))
                    {
                        Add(SessionTapeStepKind.Hook, hook.EventId, finding);
                    }
                }
            }
        }

        return flags.ToDictionary(
            pair => pair.Key,
            IReadOnlyList<Finding> (pair) => pair.Value);
    }

    static string? FindEvidence(Finding finding, string field) =>
        finding.Evidence.FirstOrDefault(item => item.Field == field)?.Value;

    static bool HasHookFailureEvidence(Finding finding) =>
        finding.Evidence.Any(item => item.Field == HookSuccessField)
        && finding.Evidence.Any(item => item.Field == HookErrorField);
}
