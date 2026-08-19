using System.Text.Json;

namespace AecoPostMortem.Ingestion;

/// <summary>The shape a <c>tool.execution_start.data.arguments</c> value actually had (FR-4).</summary>
public enum ToolArgumentKind
{
    /// <summary>Named fields, the shape every tool but <c>apply_patch</c> carries.</summary>
    Object,

    /// <summary>A bare JSON string — <c>apply_patch</c>'s whole patch envelope, not a projection of
    /// it.</summary>
    String,

    /// <summary>Neither of the two shapes this build recognises (a number, bool, array or null).
    /// Recorded rather than coerced, so a future third shape is never guessed at.</summary>
    Unparsed,
}

/// <summary>
/// <c>tool.execution_start.data.arguments</c>, parsed polymorphically (FR-4): an object for most
/// tools, a bare JSON string for <c>apply_patch</c>. A parser that assumes an object silently drops
/// every patch — PRD §3.9's first listed failure mode, because finding class 3 loses its entire
/// input, silently and without error.
/// </summary>
/// <remarks>
/// A shape neither Object nor String is recorded as <see cref="ToolArgumentKind.Unparsed"/>, never
/// coerced into either — a future tool arriving with a third argument shape must be recorded as
/// unparsed rather than guessed at. <see cref="Raw"/> preserves its text regardless of
/// <see cref="Kind"/>, so nothing is lost even when this type does not recognise the shape.
/// </remarks>
public sealed class ToolArguments
{
    readonly JsonElement? objectValue;
    readonly string? stringValue;

    ToolArguments(ToolArgumentKind kind, string raw, JsonElement? objectValue, string? stringValue)
    {
        Kind = kind;
        Raw = raw;
        this.objectValue = objectValue;
        this.stringValue = stringValue;
    }

    /// <summary>The shape the value actually had.</summary>
    public ToolArgumentKind Kind { get; }

    /// <summary>The arguments value's own JSON text, exactly as read.</summary>
    public string Raw { get; }

    /// <summary>
    /// Parse a <c>tool.execution_start.data.arguments</c> value's own JSON text — the value itself,
    /// not the envelope it sits inside.
    /// </summary>
    public static ToolArguments Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        return root.ValueKind switch
        {
            JsonValueKind.Object => new ToolArguments(ToolArgumentKind.Object, json, root.Clone(), null),
            JsonValueKind.String => new ToolArguments(ToolArgumentKind.String, json, null, root.GetString()),
            _ => new ToolArguments(ToolArgumentKind.Unparsed, json, null, null),
        };
    }

    /// <summary>A named field of an object-shaped arguments value.</summary>
    /// <exception cref="InvalidOperationException"><see cref="Kind"/> is not
    /// <see cref="ToolArgumentKind.Object"/>.</exception>
    public bool TryGetProperty(string name, out JsonElement value)
    {
        if (Kind != ToolArgumentKind.Object)
        {
            throw new InvalidOperationException(
                $"Arguments is {Kind}, not Object; named fields are not available.");
        }

        return objectValue!.Value.TryGetProperty(name, out value);
    }

    /// <summary>The whole envelope text of a string-shaped arguments value, e.g. an
    /// <c>apply_patch</c> patch.</summary>
    /// <exception cref="InvalidOperationException"><see cref="Kind"/> is not
    /// <see cref="ToolArgumentKind.String"/>.</exception>
    public string AsText =>
        Kind == ToolArgumentKind.String
            ? stringValue!
            : throw new InvalidOperationException($"Arguments is {Kind}, not String.");

    /// <summary>
    /// Re-serialise to JSON. Object and String round-trip through their typed representation;
    /// Unparsed returns <see cref="Raw"/> verbatim, because a shape this type does not recognise
    /// must never be coerced or guessed at — only ever preserved.
    /// </summary>
    public string ToJson() => Kind switch
    {
        ToolArgumentKind.Object => objectValue!.Value.GetRawText(),
        ToolArgumentKind.String => JsonSerializer.Serialize(stringValue),
        ToolArgumentKind.Unparsed => Raw,
        _ => throw new InvalidOperationException($"Unrecognised {nameof(ToolArgumentKind)} {Kind}."),
    };
}
