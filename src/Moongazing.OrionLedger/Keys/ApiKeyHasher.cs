namespace Moongazing.OrionLedger.Keys;

using System.Buffers;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// Hashes API keys for storage. Keys are high-entropy random tokens, so a fast cryptographic
/// hash (SHA-256) is the right tool: there is no low-entropy secret to protect against brute
/// force, and verification stays O(1). The plaintext key is never stored; only its hash is.
/// </summary>
public static class ApiKeyHasher
{
    // Tokens are short (prefix plus a base64url secret), so their UTF-8 form fits comfortably on the
    // stack. The threshold caps stack usage; anything larger falls back to a pooled buffer so the
    // hot verify path stays allocation-free for the common case without risking a stack overflow.
    private const int MaxStackUtf8Bytes = 256;

    /// <summary>Compute the lowercase hex SHA-256 hash of a token.</summary>
    /// <param name="token">The plaintext key.</param>
    /// <returns>A 64-character lowercase hex digest.</returns>
    public static string Hash(string token)
    {
        ArgumentException.ThrowIfNullOrEmpty(token);

        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];

        var maxBytes = Encoding.UTF8.GetMaxByteCount(token.Length);
        byte[]? rented = null;
        Span<byte> utf8 = maxBytes <= MaxStackUtf8Bytes
            ? stackalloc byte[MaxStackUtf8Bytes]
            : (rented = ArrayPool<byte>.Shared.Rent(maxBytes));

        try
        {
            var written = Encoding.UTF8.GetBytes(token, utf8);
            SHA256.HashData(utf8[..written], digest);
        }
        finally
        {
            if (rented is not null)
            {
                // The rented buffer held the plaintext token's UTF-8 bytes. Clear it on return so the
                // secret material is not leaked to the next renter of this shared pool buffer.
                ArrayPool<byte>.Shared.Return(rented, clearArray: true);
            }
        }

#if NET9_0_OR_GREATER
        return Convert.ToHexStringLower(digest);
#else
        // net8 has no Convert.ToHexStringLower. Format the digest into a stack span of lowercase hex
        // chars, then materialise the string once. This avoids the uppercase-then-ToLowerInvariant
        // double allocation of the previous implementation.
        Span<char> hex = stackalloc char[digest.Length * 2];
        ToLowerHex(digest, hex);
        return new string(hex);
#endif
    }

#if !NET9_0_OR_GREATER
    private static ReadOnlySpan<char> HexLower => "0123456789abcdef";

    private static void ToLowerHex(ReadOnlySpan<byte> bytes, Span<char> destination)
    {
        for (var i = 0; i < bytes.Length; i++)
        {
            var b = bytes[i];
            destination[i * 2] = HexLower[b >> 4];
            destination[(i * 2) + 1] = HexLower[b & 0xF];
        }
    }
#endif

    /// <summary>
    /// Constant-time comparison of two hex hashes, so verification does not leak how much of a
    /// hash matched through timing.
    /// </summary>
    /// <param name="a">The first hash.</param>
    /// <param name="b">The second hash.</param>
    /// <returns>True when the hashes are equal.</returns>
    public static bool FixedTimeEquals(string a, string b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
    }
}
