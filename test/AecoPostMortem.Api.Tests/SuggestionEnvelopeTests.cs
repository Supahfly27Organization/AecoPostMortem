using System.Text.Json;
using AecoPostMortem.Findings;

namespace AecoPostMortem.Api.Tests;

/// <summary>
/// Scenario 2 of the API response envelope (issue #13): a finding whose class has no suggestion
/// template must still serialise a suggestion field — the absence is an explicit state
/// (<see cref="SuggestionEnvelope.Absent"/>), never a missing or null field a client has to guess at.
/// </summary>
public sealed class SuggestionEnvelopeTests
{
    [Fact]
    public void Of_maps_a_null_domain_suggestion_to_the_absent_singleton()
    {
        var envelope = SuggestionEnvelope.Of(null);

        Assert.Same(SuggestionEnvelope.Absent, envelope);
    }

    [Fact]
    public void Of_maps_a_present_domain_suggestion_to_its_text()
    {
        var envelope = SuggestionEnvelope.Of(new Suggestion { Text = "name `rg`" });

        var present = Assert.IsType<SuggestionEnvelope.Present>(envelope);
        Assert.Equal("name `rg`", present.Text);
    }

    [Fact]
    public void An_absent_suggestion_serialises_as_an_explicit_state_not_a_missing_field()
    {
        var json = JsonSerializer.Serialize(SuggestionEnvelope.Absent);
        using var document = JsonDocument.Parse(json);

        // The absent state is a real, discriminated value — not null and not an empty object a
        // client could confuse with "the field was omitted".
        Assert.True(document.RootElement.TryGetProperty("state", out var state));
        Assert.Equal("absent", state.GetString());
    }

    [Fact]
    public void A_present_suggestion_round_trips_through_serialisation()
    {
        SuggestionEnvelope original = SuggestionEnvelope.Of(new Suggestion { Text = "name `rg`" });

        var json = JsonSerializer.Serialize(original);
        var roundTripped = JsonSerializer.Deserialize<SuggestionEnvelope>(json);

        var present = Assert.IsType<SuggestionEnvelope.Present>(roundTripped);
        Assert.Equal("name `rg`", present.Text);
    }
}
