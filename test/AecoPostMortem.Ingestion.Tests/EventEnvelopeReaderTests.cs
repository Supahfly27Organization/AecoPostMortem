using AecoPostMortem.Data;

namespace AecoPostMortem.Ingestion.Tests;

/// <summary>FR-8: the envelope fields <see cref="ExecutionRecordBuilder"/> reconstructs causality
/// and ownership from.</summary>
public sealed class EventEnvelopeReaderTests
{
    static RawEvent RawEventWithPayload(string payload) =>
        new("session-1", 0, "assistant.turn_start", "2026-05-07T00:00:00Z", "1.0.0", "events.jsonl", 0, "hash-0", payload);

    [Fact]
    public void Id_parentId_and_agentId_are_read_from_the_payload()
    {
        var raw = RawEventWithPayload(
            """{"type":"tool.execution_start","ts":"2026-05-07T00:00:00Z","id":"e1","parentId":"e0","agentId":"agent-1","data":{"toolName":"view"}}""");

        var ok = EventEnvelopeReader.TryRead(raw, out var envelope);

        Assert.True(ok);
        Assert.Equal("e1", envelope.Id);
        Assert.Equal("e0", envelope.ParentId);
        Assert.Equal("agent-1", envelope.AgentId);
        Assert.Equal("view", envelope.Data.GetProperty("toolName").GetString());
    }

    [Fact]
    public void Absence_of_agentId_means_main_thread()
    {
        var raw = RawEventWithPayload("""{"type":"assistant.turn_start","ts":"2026-05-07T00:00:00Z","id":"e1","parentId":null,"data":{"turnId":"turn-1"}}""");

        var ok = EventEnvelopeReader.TryRead(raw, out var envelope);

        Assert.True(ok);
        Assert.Null(envelope.AgentId);
    }

    [Fact]
    public void A_missing_id_cannot_take_part_in_the_causality_chain()
    {
        var raw = RawEventWithPayload("""{"type":"assistant.turn_start","ts":"2026-05-07T00:00:00Z","data":{}}""");

        var ok = EventEnvelopeReader.TryRead(raw, out _);

        Assert.False(ok);
    }
}
