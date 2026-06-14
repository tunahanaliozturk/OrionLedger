namespace Moongazing.OrionLedger.Keys;

using System.Security.Cryptography;
using System.Text;

/// <summary>
/// Hashes API keys for storage. Keys are high-entropy random tokens, so a fast cryptographic
/// hash (SHA-256) is the right tool: there is no low-entropy secret to protect against brute
/// force, and verification stays O(1). The plaintext key is never stored; only its hash is.
/// </summary>
public static class ApiKeyHasher
{
    /// <summary>Compute the lowercase hex SHA-256 hash of a token.</summary>
    /// <param name="token">The plaintext key.</param>
    /// <returns>A 64-character lowercase hex digest.</returns>
    public static string Hash(string token)
    {
        ArgumentException.ThrowIfNullOrEmpty(token);
        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(Encoding.UTF8.GetBytes(token), digest);
        return Convert.ToHexString(digest).ToLowerInvariant();
    }

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
