namespace AecoPostMortem.Data;

/// <summary>
/// The physical names of the system-prompt dedup table. Stated once, the same way
/// <see cref="RawEventSchema"/> states RAW's, so the model mapping and the batched append cannot
/// drift apart.
/// </summary>
public static class SystemPromptTextSchema
{
    public const string Table = "system_prompt_text";

    public const string ContentHash = "content_hash";
    public const string Text = "text";

    /// <summary>Every column the append path writes, in the order it writes them.</summary>
    public static IReadOnlyList<string> WrittenColumns { get; } = [ContentHash, Text];
}
