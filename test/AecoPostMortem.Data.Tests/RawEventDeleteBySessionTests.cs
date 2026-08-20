namespace AecoPostMortem.Data.Tests;

/// <summary>
/// FR-7's retroactive edge case: a session already ingested before its cwd was added to the
/// exclusion list must be removable from RAW too, not just refused prospectively.
/// <see cref="RawEventBatch.DeleteBySession"/> is the removal <c>AecoPostMortem.Ingestion.
/// SessionIngestor.Ingest</c> calls once it decides a session is excluded.
/// </summary>
public sealed class RawEventDeleteBySessionTests
{
    [Fact]
    public void Every_row_for_the_session_is_removed_and_the_count_is_returned()
    {
        using var temporary = new TemporaryStore();
        using var context = temporary.Store.Open();

        const string session2SourceFile = @"~/.copilot/session-state/session-2/events.jsonl";

        RawEventBatch.Append(context, [
            Events.From("""{"type":"session.start"}""", sessionId: "session-1", sequence: 0, byteOffset: 0),
            Events.From("""{"type":"assistant.turn_start"}""", sessionId: "session-1", sequence: 1, byteOffset: 64),
            Events.From("""{"type":"session.start"}""", sessionId: "session-2", sequence: 0, byteOffset: 0, sourceFile: session2SourceFile),
        ]);

        var deleted = RawEventBatch.DeleteBySession(context, "session-1");

        Assert.Equal(2, deleted);
        Assert.Equal(1, context.RawEvents.Count());
        Assert.All(context.RawEvents, row => Assert.Equal("session-2", row.SessionId));
    }

    [Fact]
    public void A_session_with_nothing_stored_deletes_nothing_and_does_not_throw()
    {
        using var temporary = new TemporaryStore();
        using var context = temporary.Store.Open();

        var deleted = RawEventBatch.DeleteBySession(context, "no-such-session");

        Assert.Equal(0, deleted);
    }
}
