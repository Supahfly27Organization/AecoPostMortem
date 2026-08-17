namespace AecoPostMortem.Data.Tests;

/// <summary>
/// The batched raw-SQL append (PRD §3.1) and the idempotency FR-5 requires of it.
/// </summary>
public sealed class RawEventAppendTests
{
    [Fact]
    public void Re_appending_the_same_lines_adds_nothing()
    {
        using var temporary = new TemporaryStore();
        var lines = Lines(120).ToArray();

        using var context = temporary.Store.Open();

        Assert.Equal(120, RawEventBatch.Append(context, lines));
        Assert.Equal(0, RawEventBatch.Append(context, lines));
        Assert.Equal(120, context.RawEvents.Count());
    }

    /// <summary>
    /// Identity is FR-2's triple, so the same bytes at a different offset are a different event —
    /// the corpus repeats identical lines and collapsing them would lose real events.
    /// </summary>
    [Fact]
    public void The_same_bytes_at_a_different_offset_are_a_different_event()
    {
        using var temporary = new TemporaryStore();
        const string payload = """{"type":"assistant.turn_start"}""";

        using var context = temporary.Store.Open();

        var inserted = RawEventBatch.Append(context, [
            Events.From(payload, sequence: 0, byteOffset: 0),
            Events.From(payload, sequence: 1, byteOffset: 512),
        ]);

        Assert.Equal(2, inserted);
    }

    /// <summary>
    /// More rows than fit in one statement, so the batching seam itself is covered rather than
    /// assumed. The measured full ingest is 56,138 rows (PRD §3.1); this is the same seam, walked
    /// enough times to catch a tail batch that is dropped or double-counted.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(RawEventBatch.DefaultRowsPerStatement)]
    [InlineData(RawEventBatch.DefaultRowsPerStatement + 1)]
    [InlineData((RawEventBatch.DefaultRowsPerStatement * 3) + 7)]
    public void Every_row_lands_however_the_batches_fall(int count)
    {
        using var temporary = new TemporaryStore();

        using var context = temporary.Store.Open();

        Assert.Equal(count, RawEventBatch.Append(context, Lines(count)));
        Assert.Equal(count, context.RawEvents.Count());
    }

    /// <summary>The sequence number is stored as an integer, not as text: an index over a
    /// lexically-ordered column would put event 10 before event 9 on the Flight Recorder's tape.</summary>
    [Fact]
    public void Sequence_numbers_order_numerically()
    {
        using var temporary = new TemporaryStore();

        using var context = temporary.Store.Open();
        RawEventBatch.Append(context, Lines(12));

        var sequences = context.RawEvents
            .OrderBy(row => row.Sequence)
            .Select(row => row.Sequence)
            .ToArray();

        Assert.Equal(Enumerable.Range(0, 12).Select(n => (long)n), sequences);
    }

    static IEnumerable<RawEvent> Lines(int count) =>
        Enumerable.Range(0, count).Select(n => Events.From(
            $$$"""{"type":"tool.execution_start","data":{"n":{{{n}}}}}""",
            sequence: n,
            byteOffset: n * 128L));
}
