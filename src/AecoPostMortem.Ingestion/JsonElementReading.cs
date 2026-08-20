using System.Text.Json;

namespace AecoPostMortem.Ingestion;

/// <summary>
/// The one string-reading pattern <see cref="SessionBuilder"/>, <see cref="SkillBuilder"/> and
/// <see cref="HookBuilder"/> each need over a RAW event's own <c>data</c> object: guard
/// <see cref="JsonValueKind.Object"/>, then read a string property or fall through to absent.
/// Shared here rather than copied three times — <see cref="ExecutionRecordBuilder"/>'s own private
/// <c>GetString</c>/<c>GetLong</c>/<c>GetInt</c>/<c>GetBool</c> family stays where it is: it reads
/// more shapes than a string alone and is cohesive with the rest of that file's parsing.
/// </summary>
static class JsonElementReading
{
    public static string? StringOrNull(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    public static string StringOrEmpty(JsonElement element, string property) =>
        StringOrNull(element, property) ?? string.Empty;
}
