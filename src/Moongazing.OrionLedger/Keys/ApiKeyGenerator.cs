namespace Moongazing.OrionLedger.Keys;

using System.Security.Cryptography;

/// <summary>
/// Generates API key tokens of the form <c>&lt;prefix&gt;&lt;base64url-secret&gt;</c>, where the secret is
/// cryptographically random. The token is returned in plaintext exactly once at issuance; only
/// its hash is persisted.
/// </summary>
public static class ApiKeyGenerator
{
    /// <summary>The number of leading characters retained as a non-secret display prefix.</summary>
    public const int DisplayPrefixLength = 12;

    /// <summary>Generate a new token.</summary>
    /// <param name="prefix">The configured key prefix.</param>
    /// <param name="secretByteLength">The number of random bytes in the secret.</param>
    /// <returns>The plaintext token.</returns>
    public static string Generate(string prefix, int secretByteLength)
    {
        ArgumentException.ThrowIfNullOrEmpty(prefix);
        if (secretByteLength < 16)
        {
            throw new ArgumentOutOfRangeException(nameof(secretByteLength), secretByteLength,
                "secretByteLength must be at least 16.");
        }

        var bytes = RandomNumberGenerator.GetBytes(secretByteLength);
        return prefix + Base64UrlEncode(bytes);
    }

    /// <summary>
    /// The non-secret display prefix of a token (the leading <see cref="DisplayPrefixLength"/>
    /// characters), shown to admins to recognise a key without exposing the secret.
    /// </summary>
    /// <param name="token">The plaintext token.</param>
    /// <returns>The display prefix.</returns>
    public static string DisplayPrefix(string token)
    {
        ArgumentException.ThrowIfNullOrEmpty(token);
        return token.Length <= DisplayPrefixLength ? token : token[..DisplayPrefixLength];
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
