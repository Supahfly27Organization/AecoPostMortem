namespace AecoPostMortem.Data.Tests;

/// <summary>
/// FR-5's second line of defence: byte offsets are safe identity only because growth is
/// append-only (PRD §3.2 — "on all 8 events carrying eventsFileSizeBytes, the declared value
/// equals the byte offset at which that event begins, delta 0"). A file rewritten rather than
/// appended to breaks that assumption, and <see cref="RawEventBatch.DetectRewrites"/> is what
/// notices before anything is appended over it.
/// </summary>
public sealed class RawEventRewriteDetectionTests
{
    const string SourceFile = @"~/.copilot/session-state/session-1/events.jsonl";

    [Fact]
    public void A_different_payload_at_an_already_stored_offset_is_reported_as_a_mismatch()
    {
        using var temporary = new TemporaryStore();
        var original = Events.From("""{"type":"session.start"}""", sourceFile: SourceFile, byteOffset: 0);
        var rewritten = Events.From("""{"type":"session.start","data":{"rewritten":true}}""", sourceFile: SourceFile, byteOffset: 0);

        using var context = temporary.Store.Open();
        RawEventBatch.Append(context, [original]);

        var mismatches = RawEventBatch.DetectRewrites(context, [rewritten]);

        var mismatch = Assert.Single(mismatches);
        Assert.Equal(SourceFile, mismatch.SourceFile);
        Assert.Equal(0, mismatch.ByteOffset);
        Assert.Equal(original.ContentHash, mismatch.StoredContentHash);
        Assert.Equal(rewritten.ContentHash, mismatch.ReadContentHash);
    }

    [Fact]
    public void The_same_payload_at_an_already_stored_offset_is_not_a_mismatch()
    {
        using var temporary = new TemporaryStore();
        var original = Events.From("""{"type":"session.start"}""", sourceFile: SourceFile, byteOffset: 0);

        using var context = temporary.Store.Open();
        RawEventBatch.Append(context, [original]);

        var mismatches = RawEventBatch.DetectRewrites(context, [original]);

        Assert.Empty(mismatches);
    }

    [Fact]
    public void A_new_offset_that_was_never_stored_is_not_a_mismatch()
    {
        using var temporary = new TemporaryStore();
        var original = Events.From("""{"type":"session.start"}""", sourceFile: SourceFile, byteOffset: 0);
        var grown = Events.From("""{"type":"assistant.turn_start"}""", sourceFile: SourceFile, byteOffset: 64, sequence: 1);

        using var context = temporary.Store.Open();
        RawEventBatch.Append(context, [original]);

        var mismatches = RawEventBatch.DetectRewrites(context, [grown]);

        Assert.Empty(mismatches);
    }

    [Fact]
    public void An_offset_stored_under_a_different_source_file_never_matches()
    {
        using var temporary = new TemporaryStore();
        var original = Events.From("""{"type":"session.start"}""", sourceFile: SourceFile, byteOffset: 0);
        var sameOffsetDifferentFile = Events.From(
            """{"type":"session.start","data":{"other":true}}""",
            sourceFile: @"~/.copilot/session-state/session-2/events.jsonl",
            byteOffset: 0);

        using var context = temporary.Store.Open();
        RawEventBatch.Append(context, [original]);

        var mismatches = RawEventBatch.DetectRewrites(context, [sameOffsetDifferentFile]);

        Assert.Empty(mismatches);
    }

    /// <summary>A rewrite is not only ever one line deep: every diverging offset is reported, not
    /// just the first one found.</summary>
    [Fact]
    public void Every_diverging_offset_is_reported_not_just_the_first()
    {
        using var temporary = new TemporaryStore();
        var first = Events.From("""{"type":"session.start"}""", sourceFile: SourceFile, byteOffset: 0);
        var second = Events.From("""{"type":"assistant.turn_start"}""", sourceFile: SourceFile, byteOffset: 64, sequence: 1);

        var rewrittenFirst = Events.From("""{"type":"session.start","data":{"rewritten":true}}""", sourceFile: SourceFile, byteOffset: 0);
        var rewrittenSecond = Events.From("""{"type":"assistant.turn_start","data":{"rewritten":true}}""", sourceFile: SourceFile, byteOffset: 64, sequence: 1);

        using var context = temporary.Store.Open();
        RawEventBatch.Append(context, [first, second]);

        var mismatches = RawEventBatch.DetectRewrites(context, [rewrittenFirst, rewrittenSecond]);

        Assert.Equal(2, mismatches.Count);
        Assert.Contains(mismatches, m => m.ByteOffset == 0);
        Assert.Contains(mismatches, m => m.ByteOffset == 64);
    }

    [Fact]
    public void An_empty_batch_reports_no_mismatches()
    {
        using var temporary = new TemporaryStore();

        using var context = temporary.Store.Open();

        var mismatches = RawEventBatch.DetectRewrites(context, []);

        Assert.Empty(mismatches);
    }
}
