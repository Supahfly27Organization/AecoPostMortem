using System.Security.Cryptography;
using System.Text;

namespace AecoPostMortem.Rules;

/// <summary>
/// FR-27's content hash: reduces a session's block set to one hash that identifies its rule-set
/// version. Deliberately order-insensitive over the blocks themselves — PRD Part 8 Q4 records that
/// whether block ordering is stable across sessions was not measured, so this hash cannot assume a
/// naive ordered concatenation would agree between two sessions carrying the identical set. Each
/// block's own statements are hashed in the order <see cref="RuleStatementExtractor"/> recovered
/// them, because that order is intrinsic to the source document, not an artifact of how sessions
/// happened to carry blocks.
/// </summary>
public static class RuleSetVersionHasher
{
    public static string ComputeHash(IReadOnlyList<InstructionBlock> blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);

        var canonicalBlocks = blocks
            .Select(CanonicalizeBlock)
            .Order(StringComparer.Ordinal) // order-insensitive: the same block set hashes
                                            // identically regardless of the order it was carried in
            .ToArray();

        var canonical = string.Concat(canonicalBlocks.Select(LengthPrefixed));
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(bytes);
    }

    static string CanonicalizeBlock(InstructionBlock block)
    {
        var fields = new[] { block.SourceFile }.Concat(block.Statements.Select(s => s.Text));
        return string.Concat(fields.Select(LengthPrefixed));
    }

    /// <summary>
    /// Length-prefixes <paramref name="value"/> (netstring-style: <c>"{length}:{value}"</c>) rather
    /// than joining fields with a separator character. A separator character can only be trusted not
    /// to collide if it is guaranteed absent from every field's own content — extracted rule text is
    /// arbitrary and unvalidated, so that guarantee does not hold (an earlier version of this hasher
    /// joined fields with ASCII control characters on that unenforced assumption). Length-prefixing
    /// instead makes the encoding of a sequence of fields injective regardless of what those fields
    /// contain: two different (source file, statements) sequences can never encode to the same
    /// string, so two different block sets can never collide to the same hash.
    /// </summary>
    static string LengthPrefixed(string value) => $"{value.Length}:{value}";
}
