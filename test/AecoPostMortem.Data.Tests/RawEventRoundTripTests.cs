using System.Text;
using System.Text.Json;

namespace AecoPostMortem.Data.Tests;

/// <summary>
/// FR-2, and Phase A's exit criterion behind it (PRD §3.5): a line that goes into RAW comes back
/// out byte-identical, including the fields no parser in this build recognises.
/// </summary>
public sealed class RawEventRoundTripTests
{
    /// <summary>
    /// The longest system prompt in the frozen corpus, measured at 59,982 characters
    /// (<c>docs/product-superpowers/discovery/2026-08-16-copilot-ingestion-data-map.md</c> Part 6).
    /// The round trip has to hold at the largest size the corpus actually contains, not at the
    /// size a test author finds convenient.
    /// </summary>
    const int LongestMeasuredSystemPrompt = 59_982;

    public static TheoryData<string, string> Payloads() => new()
    {
        {
            "plain ASCII",
            """{"type":"session.start","data":{"copilotVersion":"0.0.339"}}"""
        },
        {
            "non-ASCII, including astral-plane and combining characters",
            """{"type":"assistant.message","data":{"text":"café ☕ 日本語 🇬🇧 é —"}}"""
        },
        {
            "escaped newlines inside a JSON string",
            """{"type":"tool.execution_start","data":{"arguments":"*** Begin Patch\n--- a.cs\n+++ b.cs\n"}}"""
        },
        {
            "a literal newline in the stored text",
            "{\"type\":\"note\",\"data\":{\"text\":\"first\nsecond\"}}"
        },
        {
            "a string-valued arguments field, the apply_patch shape (FR-4)",
            """{"type":"tool.execution_start","data":{"name":"apply_patch","arguments":"*** Begin Patch"}}"""
        },
    };

    [Theory]
    [MemberData(nameof(Payloads))]
    public void A_payload_comes_back_out_of_RAW_byte_identical(string description, string payload)
    {
        using var temporary = new TemporaryStore();
        var original = RawPayload.ToUtf8(payload);

        using (var context = temporary.Store.Open())
        {
            RawEventBatch.Append(context, [Events.From(payload)]);
        }

        using var reopened = temporary.Store.Open();
        var stored = reopened.RawEvents.Single();

        Assert.True(
            RawPayload.ToUtf8(stored.Payload).AsSpan().SequenceEqual(original),
            $"The {description} payload did not round-trip byte-identically.");
    }

    [Fact]
    public void The_largest_measured_system_prompt_round_trips()
    {
        using var temporary = new TemporaryStore();
        var payload = SystemMessageOf(LongestMeasuredSystemPrompt);
        var original = RawPayload.ToUtf8(payload);

        Assert.True(payload.Length >= LongestMeasuredSystemPrompt);

        using (var context = temporary.Store.Open())
        {
            RawEventBatch.Append(context, [Events.From(payload, eventType: "system.message")]);
        }

        using var reopened = temporary.Store.Open();

        Assert.True(RawPayload.ToUtf8(reopened.RawEvents.Single().Payload).AsSpan().SequenceEqual(original));
    }

    [Fact]
    public void A_field_no_parser_recognises_survives_the_round_trip()
    {
        using var temporary = new TemporaryStore();
        const string payload =
            """{"type":"session.start","data":{"copilotVersion":"0.0.339"},"anUnknownField":{"nested":[1,2,3]}}""";

        using (var context = temporary.Store.Open())
        {
            RawEventBatch.Append(context, [Events.From(payload)]);
        }

        using var reopened = temporary.Store.Open();
        using var parsed = JsonDocument.Parse(reopened.RawEvents.Single().Payload);

        Assert.True(
            parsed.RootElement.TryGetProperty("anUnknownField", out var unknown),
            "An unrecognised field was dropped; RAW is required to preserve it (FR-2, PRD §3.2).");
        Assert.Equal(3, unknown.GetProperty("nested").GetArrayLength());
    }

    [Fact]
    public void The_row_carries_the_provenance_the_replay_needs()
    {
        using var temporary = new TemporaryStore();
        const string payload = """{"type":"session.start"}""";
        var appended = Events.From(payload, sourceFile: "/logs/events.jsonl", byteOffset: 4_096);

        using (var context = temporary.Store.Open())
        {
            RawEventBatch.Append(context, [appended]);
        }

        using var reopened = temporary.Store.Open();
        var stored = reopened.RawEvents.Single();

        Assert.Equal("/logs/events.jsonl", stored.SourceFile);
        Assert.Equal(4_096, stored.ByteOffset);
        Assert.Equal(Events.ProviderVersion, stored.ProviderVersion);
        Assert.Equal(RawPayload.ContentHash(RawPayload.ToUtf8(payload)), stored.ContentHash);
    }

    [Fact]
    public void A_line_that_is_not_valid_UTF8_is_refused_rather_than_stored_lossily()
    {
        // 0xC3 opens a two-byte sequence and 0x28 cannot continue it. Substituting U+FFFD here
        // would store a row that can never replay; the decode fails instead, which is what routes
        // the line to FR-6's skipped-line count.
        byte[] invalid = [0x7B, 0xC3, 0x28, 0x7D];

        Assert.Throws<DecoderFallbackException>(() => RawPayload.FromUtf8(invalid));
    }

    static string SystemMessageOf(int characters)
    {
        var body = new StringBuilder(characters);
        while (body.Length < characters)
        {
            body.Append("Repo Rule 6: nothing in Rules may name a tool. ");
        }

        return JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["type"] = "system.message",
            ["data"] = new Dictionary<string, string> { ["content"] = body.ToString(0, characters) },
        });
    }
}
