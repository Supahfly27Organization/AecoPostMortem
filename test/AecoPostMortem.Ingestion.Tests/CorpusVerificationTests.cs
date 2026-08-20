using Microsoft.EntityFrameworkCore;

namespace AecoPostMortem.Ingestion.Tests;

/// <summary>
/// S-45 (issue #9), Phase A's exit criterion (PRD §3.5): "every session reconstructs; a re-run adds
/// no duplicate events; RAW replays byte-identically; and the event census reproduces the frozen
/// fixture corpus's post-exclusion census." A verification story, not a feature story — it asserts
/// properties of the already-merged ingestion path (<c>SessionDiscovery</c>, <c>SessionIngestor</c>,
/// issue #5/#7) against the frozen fixture (FR-55, <c>fixtures/corpus-manifest.json</c>), never
/// against a number typed into a test — the manifest is what lets the corpus grow without this file
/// changing.
/// </summary>
public sealed class CorpusVerificationTests : IClassFixture<CorpusIngestFixture>
{
    readonly CorpusIngestFixture _fixture;

    public CorpusVerificationTests(CorpusIngestFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>Scenario: the event census reproduces the reference counts. Every type the frozen
    /// manifest's <c>totals.event_census</c> recorded must appear in RAW with the same count, and any
    /// type present on disk but absent from the store fails the check — the reverse direction
    /// matters just as much, since a parser that silently drops a whole event type would otherwise
    /// look identical to one that ingested everything.</summary>
    [Fact]
    public void The_RAW_event_census_matches_the_frozen_manifests_reference_counts()
    {
        if (!_fixture.Available)
        {
            Assert.Skip(
                $"No corpus at {_fixture.Source ?? "(unresolved)"} on this machine; the gate only "
                + "runs where the corpus does.");
            return;
        }

        var manifestPath = ReferenceCorpus.ManifestPath();
        Assert.NotNull(manifestPath);

        using var manifest = System.Text.Json.JsonDocument.Parse(File.ReadAllText(manifestPath!));
        var expectedCensus = manifest.RootElement
            .GetProperty("totals")
            .GetProperty("event_census")
            .EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.GetInt64());

        var actualCensus = _fixture.Context!.RawEvents
            .GroupBy(raw => raw.EventType)
            .ToDictionary(group => group.Key, group => (long)group.Count());

        var missingFromStore = expectedCensus.Keys.Except(actualCensus.Keys).Order(StringComparer.Ordinal).ToArray();
        var extraInStore = actualCensus.Keys.Except(expectedCensus.Keys).Order(StringComparer.Ordinal).ToArray();

        Assert.True(
            missingFromStore.Length == 0,
            $"{missingFromStore.Length} event type(s) present in the frozen manifest but absent "
            + $"from RAW: {string.Join(", ", missingFromStore)}");
        Assert.True(
            extraInStore.Length == 0,
            $"{extraInStore.Length} event type(s) present in RAW but absent from the frozen "
            + $"manifest: {string.Join(", ", extraInStore)}");

        var mismatches = expectedCensus
            .Where(pair => actualCensus.TryGetValue(pair.Key, out var actual) && actual != pair.Value)
            .Select(pair => $"{pair.Key}: expected {pair.Value}, got {actualCensus[pair.Key]}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            mismatches.Length == 0,
            $"{mismatches.Length} event type(s) whose RAW count disagrees with the frozen manifest:\n"
            + string.Join('\n', mismatches));
    }

    /// <summary>Scenario: RAW replays byte-identically across the whole corpus. Every RAW row's
    /// <see cref="AecoPostMortem.Data.RawEvent.Payload"/> is re-serialised to UTF-8 and compared to
    /// the exact bytes the source file carries at that row's own byte offset — independently
    /// re-derived here by splitting each source file on <c>\n</c>, not by trusting
    /// <see cref="SessionEventReader"/>'s own split, so this proves the stored row still matches disk
    /// rather than merely that the reader agrees with itself. A single mismatch fails the check.
    /// </summary>
    [Fact]
    public void Every_RAW_row_re_serialises_byte_identically_to_its_source_line()
    {
        if (!_fixture.Available)
        {
            Assert.Skip(
                $"No corpus at {_fixture.Source ?? "(unresolved)"} on this machine; the gate only "
                + "runs where the corpus does.");
            return;
        }

        var rows = _fixture.Context!.RawEvents.AsNoTracking().ToArray();
        Assert.True(rows.Length > 0, "No RAW rows were ingested — the gate would be vacuous.");

        var failures = new List<string>();
        var lineCache = new Dictionary<string, IReadOnlyDictionary<long, byte[]>>(StringComparer.Ordinal);

        foreach (var row in rows)
        {
            if (!lineCache.TryGetValue(row.SourceFile, out var linesByOffset))
            {
                linesByOffset = LinesByOffset(row.SourceFile);
                lineCache[row.SourceFile] = linesByOffset;
            }

            if (!linesByOffset.TryGetValue(row.ByteOffset, out var expectedBytes))
            {
                failures.Add($"{row.SourceFile}@{row.ByteOffset}: no source line at that offset");
                continue;
            }

            var actualBytes = AecoPostMortem.Data.RawPayload.ToUtf8(row.Payload);
            if (!actualBytes.AsSpan().SequenceEqual(expectedBytes))
            {
                failures.Add($"{row.SourceFile}@{row.ByteOffset}: stored payload does not match the source line");
            }
        }

        Assert.True(
            failures.Count == 0,
            $"{failures.Count} of {rows.Length} RAW row(s) failed to replay byte-identically:\n"
            + string.Join('\n', failures.Take(10)));
    }

    /// <summary>Independently re-derives "byte offset of a complete line -> that line's own bytes,
    /// excluding the terminating <c>\n</c>" for one source file, the same split rule FR-2 states
    /// (<c>\n</c> only) but implemented separately from <see cref="SessionEventReader"/> so this test
    /// is not just checking the reader against itself.</summary>
    static IReadOnlyDictionary<long, byte[]> LinesByOffset(string sourceFile)
    {
        var bytes = File.ReadAllBytes(sourceFile);
        var result = new Dictionary<long, byte[]>();

        var start = 0;
        for (var i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] != (byte)'\n')
            {
                continue;
            }

            result[start] = bytes[start..i];
            start = i + 1;
        }

        return result;
    }

    /// <summary>Scenario: a full ingest meets its time target. PRD §3.7: "full ingest of the measured
    /// 176.7 MB corpus in under 3 minutes ... [is a] target, not measurement — and FR-55's fixture is
    /// what they are measured against, so a miss is visible rather than absorbed." A miss here is
    /// reported by the assertion's own message, which states the measured elapsed time against the
    /// target — a conversation about the target, not a silently swallowed failure.</summary>
    [Fact]
    public void A_full_ingest_from_an_empty_store_completes_inside_the_PRD_3_7_target()
    {
        if (!_fixture.Available)
        {
            Assert.Skip(
                $"No corpus at {_fixture.Source ?? "(unresolved)"} on this machine; the gate only "
                + "runs where the corpus does.");
            return;
        }

        var target = TimeSpan.FromMinutes(3);
        Assert.True(
            _fixture.FullIngestElapsed < target,
            $"Full ingest of {_fixture.Sessions.Count} session(s) took {_fixture.FullIngestElapsed} "
            + $"— PRD §3.7's target is under {target}. A miss is a conversation about the target, "
            + "not a silent pass.");
    }

    /// <summary>Scenario: an incremental re-ingest meets its time target. PRD §3.7: "an incremental
    /// re-ingest in under 15 seconds" — measured here as a second ingestion pass over the same,
    /// unchanged sessions immediately after the full ingest, which must also insert nothing new
    /// (FR-5's idempotency, already covered by <c>SessionIngestorTests</c>, restated here as a
    /// sanity check that this timing run is measuring a real no-op re-ingest and not a second full
    /// one).</summary>
    [Fact]
    public void An_incremental_reingest_with_no_new_events_completes_inside_the_PRD_3_7_target()
    {
        if (!_fixture.Available)
        {
            Assert.Skip(
                $"No corpus at {_fixture.Source ?? "(unresolved)"} on this machine; the gate only "
                + "runs where the corpus does.");
            return;
        }

        Assert.Equal(0, _fixture.EventsInsertedSecondPass);

        var target = TimeSpan.FromSeconds(15);
        Assert.True(
            _fixture.IncrementalReingestElapsed < target,
            $"Incremental re-ingest of {_fixture.Sessions.Count} session(s) with no new events took "
            + $"{_fixture.IncrementalReingestElapsed} — PRD §3.7's target is under {target}. A miss "
            + "is a conversation about the target, not a silent pass.");
    }
}
