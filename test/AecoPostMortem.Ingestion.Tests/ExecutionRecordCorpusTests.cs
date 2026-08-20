using AecoPostMortem.Data;
using AecoPostMortem.Findings;
using Microsoft.EntityFrameworkCore;

namespace AecoPostMortem.Ingestion.Tests;

/// <summary>
/// Issue #7, Scenario 3, against the corpus its own <c>Given</c> names: "Given the reference
/// corpus / When agents are reconstructed / Then each <c>subagent.started</c> resolves to the task
/// call that produced it." <see cref="ExecutionRecordBuilderTests"/> proves the resolution rule
/// against hand-built events; this proves it against the real bytes, which is the only place the
/// measured 470-of-470 claim can actually be checked. The distinction matters here for the same
/// reason it did for <c>EventEnvelopeParserV1</c>'s <c>ts</c>/<c>timestamp</c> bug
/// (this project's CLAUDE.md): every synthetic fixture can agree on a wrong shape by construction.
/// </summary>
/// <remarks>
/// Skips rather than fails where the live corpus is not on the machine, the same shape
/// <see cref="CorpusVerificationTests"/> and <c>ApplyPatchCorpusRoundTripTests</c> already use, and
/// shares the one <see cref="CorpusIngestFixture"/> ingest rather than driving a second.
/// </remarks>
public sealed class ExecutionRecordCorpusTests : IClassFixture<CorpusIngestFixture>
{
    readonly CorpusIngestFixture _fixture;

    public ExecutionRecordCorpusTests(CorpusIngestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void Every_spawn_in_the_reference_corpus_resolves_to_its_spawning_call()
    {
        if (!_fixture.Available)
        {
            Assert.Skip(
                $"No corpus at {_fixture.Source ?? "(unresolved)"} on this machine; the gate only "
                + "runs where the corpus does.");
            return;
        }

        var checks = ReconstructEverySession();

        var examined = checks.Sum(check => check.Population);
        var unresolved = checks.Sum(check => check.FindingCount);

        // Against the frozen manifest, not a number typed here: the corpus can grow without this
        // file changing, and a spawn event the builder never even examined fails just as loudly as
        // one it examined and could not resolve.
        var expectedSpawns = FrozenEventCount("subagent.started");
        Assert.Equal(expectedSpawns, examined);

        Assert.True(
            unresolved == 0,
            $"{unresolved} of {examined} subagent.started event(s) in the reference corpus did not "
            + "resolve to a task call. A non-resolving spawn is a real signal (issue #7's edge "
            + "case), not a test to relax.");
    }

    /// <summary>Scenario 4 restated where it is not vacuous: the check registers itself for every
    /// session reconstructed, including the many that spawn nothing at all — a check that only
    /// appeared when it had something to say would be exactly the silent check S-37's surface
    /// exists to prevent.</summary>
    [Fact]
    public void The_spawn_resolution_check_registers_itself_for_every_session_reconstructed()
    {
        if (!_fixture.Available)
        {
            Assert.Skip(
                $"No corpus at {_fixture.Source ?? "(unresolved)"} on this machine; the gate only "
                + "runs where the corpus does.");
            return;
        }

        var checks = ReconstructEverySession();

        Assert.Equal(_fixture.Sessions.Count, checks.Count);
        Assert.All(checks, check =>
        {
            Assert.Equal(SpawnResolutionCheck.CheckId, check.CheckId);
            Assert.Equal(CheckRunStatus.Ran, check.Status);
        });

        Assert.Contains(checks, check => check.Population == 0);
    }

    /// <summary>Rebuilds every ingested session's execution record off the shared fixture's store and
    /// returns each one's spawn-resolution check. Reads RAW once and groups in memory rather than
    /// issuing one query per session — the reconstruction itself is what is under test, not the
    /// query plan.</summary>
    IReadOnlyList<CheckRegistryEntry> ReconstructEverySession()
    {
        var eventsBySession = _fixture.Context!.RawEvents
            .AsNoTracking()
            .ToArray()
            .GroupBy(raw => raw.SessionId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<RawEvent>)group.ToArray(), StringComparer.Ordinal);

        return _fixture.Sessions
            .Select(session => ExecutionRecordBuilder
                .Build(session.SessionId, eventsBySession.GetValueOrDefault(session.SessionId, []))
                .SpawnResolutionCheck)
            .ToArray();
    }

    static int FrozenEventCount(string eventType)
    {
        var manifestPath = ReferenceCorpus.ManifestPath();
        Assert.NotNull(manifestPath);

        using var manifest = System.Text.Json.JsonDocument.Parse(File.ReadAllText(manifestPath!));
        return manifest.RootElement
            .GetProperty("totals")
            .GetProperty("event_census")
            .GetProperty(eventType)
            .GetInt32();
    }
}
