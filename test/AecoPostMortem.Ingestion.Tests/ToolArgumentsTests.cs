using System.Text.Json;

namespace AecoPostMortem.Ingestion.Tests;

/// <summary>
/// FR-4: <c>tool.execution_start.data.arguments</c> is polymorphic — an object for most tools, a
/// bare JSON string for <c>apply_patch</c>'s whole patch envelope. A parser that assumes an object
/// silently drops every patch (PRD §3.9's first listed failure mode), so this type never coerces:
/// it records what shape the value actually was.
/// </summary>
public sealed class ToolArgumentsTests
{
    // A real apply_patch envelope shape, trimmed from the reference corpus.
    const string PatchEnvelope =
        "*** Begin Patch\n*** Add File: a.txt\n+hello\n*** End Patch";

    [Fact]
    public void A_string_shaped_arguments_value_preserves_its_text_intact()
    {
        var json = JsonSerializer.Serialize(PatchEnvelope);

        var arguments = ToolArguments.Parse(json);

        Assert.Equal(ToolArgumentKind.String, arguments.Kind);
        Assert.Equal(PatchEnvelope, arguments.AsText);
    }

    [Fact]
    public void An_object_shaped_arguments_value_exposes_its_named_fields_individually()
    {
        const string json = """{"path":"src/Foo.cs","offset":42}""";

        var arguments = ToolArguments.Parse(json);

        Assert.Equal(ToolArgumentKind.Object, arguments.Kind);
        Assert.True(arguments.TryGetProperty("path", out var path));
        Assert.Equal("src/Foo.cs", path.GetString());
        Assert.True(arguments.TryGetProperty("offset", out var offset));
        Assert.Equal(42, offset.GetInt32());
    }

    [Fact]
    public void A_field_the_object_does_not_carry_is_reported_absent_not_thrown()
    {
        var arguments = ToolArguments.Parse("""{"path":"src/Foo.cs"}""");

        Assert.False(arguments.TryGetProperty("missing", out _));
    }

    [Theory]
    [InlineData("42")]
    [InlineData("true")]
    [InlineData("null")]
    [InlineData("[1,2,3]")]
    public void A_shape_that_is_neither_object_nor_string_is_recorded_as_unparsed_not_coerced(string json)
    {
        var arguments = ToolArguments.Parse(json);

        Assert.Equal(ToolArgumentKind.Unparsed, arguments.Kind);
        Assert.Equal(json, arguments.Raw);
    }

    [Fact]
    public void Reading_named_fields_off_a_string_shaped_value_throws_instead_of_guessing()
    {
        var arguments = ToolArguments.Parse(JsonSerializer.Serialize(PatchEnvelope));

        Assert.Throws<InvalidOperationException>(() => { arguments.TryGetProperty("path", out _); });
    }

    [Fact]
    public void Reading_the_envelope_text_off_an_object_shaped_value_throws_instead_of_guessing()
    {
        var arguments = ToolArguments.Parse("""{"path":"src/Foo.cs"}""");

        Assert.Throws<InvalidOperationException>(() => { _ = arguments.AsText; });
    }

    [Fact]
    public void An_object_shaped_value_round_trips_through_reserialisation()
    {
        const string json = """{"path":"src/Foo.cs","offset":42,"nested":{"a":[1,2,3]}}""";
        var arguments = ToolArguments.Parse(json);

        var reparsed = ToolArguments.Parse(arguments.ToJson());

        Assert.Equal(ToolArgumentKind.Object, reparsed.Kind);
        Assert.True(JsonElementsAreEqual(
            JsonDocument.Parse(json).RootElement,
            JsonDocument.Parse(reparsed.ToJson()).RootElement));
    }

    [Fact]
    public void A_string_shaped_value_round_trips_through_reserialisation()
    {
        var json = JsonSerializer.Serialize(PatchEnvelope);
        var arguments = ToolArguments.Parse(json);

        var reparsed = ToolArguments.Parse(arguments.ToJson());

        Assert.Equal(ToolArgumentKind.String, reparsed.Kind);
        Assert.Equal(PatchEnvelope, reparsed.AsText);
    }

    [Fact]
    public void An_unparsed_value_round_trips_verbatim_rather_than_being_guessed_at()
    {
        const string json = "3.14159";
        var arguments = ToolArguments.Parse(json);

        Assert.Equal(json, arguments.ToJson());
    }

    static bool JsonElementsAreEqual(JsonElement a, JsonElement b) =>
        JsonSerializer.Serialize(a) == JsonSerializer.Serialize(b);
}
