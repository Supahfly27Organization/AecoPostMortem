using System.Security.Cryptography;
using System.Text;

namespace AecoPostMortem.Data;

/// <summary>
/// The verbatim line and its identity: how bytes off disk become
/// <see cref="RawEvent.Payload"/> and <see cref="RawEvent.ContentHash"/>, in one place so that
/// every writer produces the same hash for the same bytes.
/// </summary>
public static class RawPayload
{
    /// <summary>
    /// Strict UTF-8: it throws on a byte sequence it cannot decode rather than substituting U+FFFD.
    /// The payload column is TEXT — the shape the latency measurement was run against, and the one
    /// SQLite's JSON functions can read — and TEXT is stored as UTF-8, so a lossy decode would make
    /// the round trip silently non-verbatim. Failing here instead routes a non-UTF-8 line to FR-6's
    /// per-line tolerance, where it is counted and retried, rather than corrupting a stored row.
    /// </summary>
    static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>The line as text, byte-for-byte recoverable by <see cref="ToUtf8"/>.</summary>
    /// <exception cref="DecoderFallbackException">The bytes are not valid UTF-8.</exception>
    public static string FromUtf8(ReadOnlySpan<byte> line) => StrictUtf8.GetString(line);

    /// <summary>The bytes a payload came from. <c>ToUtf8(FromUtf8(bytes))</c> is <c>bytes</c>.</summary>
    public static byte[] ToUtf8(string payload) => StrictUtf8.GetBytes(payload);

    /// <summary>
    /// FR-2's content hash: SHA-256 of the line's bytes, lower-case hex. Over the bytes rather than
    /// the decoded string, because the byte stream is what the identity triple locates.
    /// </summary>
    public static string ContentHash(ReadOnlySpan<byte> line)
    {
        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(line, digest);
        return Convert.ToHexStringLower(digest);
    }

    /// <summary>The content hash of a payload already decoded to text.</summary>
    public static string ContentHashOfText(string payload) => ContentHash(ToUtf8(payload));
}
