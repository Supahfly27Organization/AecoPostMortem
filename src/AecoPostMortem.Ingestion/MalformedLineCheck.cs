using AecoPostMortem.Findings;

namespace AecoPostMortem.Ingestion;

/// <summary>
/// FR-6's own check, registering itself in issue #23's registry: it always ran once ingestion
/// completed (there is no refusal path — reading a line either parses or is counted), so its
/// <see cref="CheckRegistryEntry.Status"/> is always <see cref="CheckRunStatus.Ran"/>. Population is
/// the number of lines parsed and finding count is the number skipped, across every file the run
/// touched — present even when nothing failed, per the acceptance scenario's own wording.
/// </summary>
public static class MalformedLineCheck
{
    public const string CheckId = "malformed-line";

    public static CheckRegistryEntry From(IEnumerable<SessionReadResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        long population = 0;
        long skipped = 0;

        foreach (var result in results)
        {
            population += result.LinesRead;
            skipped += result.SkippedLines;
        }

        return new CheckRegistryEntry
        {
            CheckId = CheckId,
            Status = CheckRunStatus.Ran,
            Population = checked((int)population),
            FindingCount = checked((int)skipped),
        };
    }
}
