using AecoPostMortem.Findings;

namespace AecoPostMortem.Ingestion;

/// <summary>
/// FR-9's own check, registering itself in issue #23's registry under PRD §3.9's name for it,
/// <c>unresolvable-spawn</c>. Every <c>subagent.started</c> is examined against the <c>task</c>
/// <c>tool.execution_start</c> that should have produced it (<see cref="ExecutionRecordBuilder"/>) —
/// a measured 470 of 470 resolve in the reference corpus, so a non-resolving spawn is a real signal:
/// reported here rather than silently dropped from the reconstruction. There is no refusal path — a
/// spawn either resolves or is counted — so <see cref="CheckRegistryEntry.Status"/> is always
/// <see cref="CheckRunStatus.Ran"/>, the same shape as <see cref="MalformedLineCheck"/>.
/// </summary>
public static class SpawnResolutionCheck
{
    public const string CheckId = "unresolvable-spawn";

    public static CheckRegistryEntry From(int examined, int unresolved)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(examined);
        ArgumentOutOfRangeException.ThrowIfNegative(unresolved);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(unresolved, examined);

        return new CheckRegistryEntry
        {
            CheckId = CheckId,
            Status = CheckRunStatus.Ran,
            Population = examined,
            FindingCount = unresolved,
            Provenance = Provenance.Observed,
        };
    }
}
