namespace Moongazing.OrionLedger.Keys;

using System.Buffers;
#if NET9_0_OR_GREATER
using System.Buffers.Text;
#endif
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

        // Secret bytes and their base64 encoding are short and bounded, so keep them on the stack for
        // small secrets and fall back to a pooled buffer for larger ones. This avoids the per-issue
        // byte[] plus the intermediate string/Trim/Replace allocations of a string-based encoder.
        Span<byte> stackSecret = stackalloc byte[MaxStackSecretBytes];
        byte[]? rentedBytes = null;
        Span<byte> secret = secretByteLength <= MaxStackSecretBytes
            ? stackSecret[..secretByteLength]
            : (rentedBytes = ArrayPool<byte>.Shared.Rent(secretByteLength)).AsSpan(0, secretByteLength);

        try
        {
            RandomNumberGenerator.Fill(secret);
            return string.Concat(prefix, Base64UrlEncode(secret));
        }
        finally
        {
            // Scrub the plaintext secret bytes from the buffer before they leave scope.
            CryptographicOperations.ZeroMemory(secret);
            if (rentedBytes is not null)
            {
                ArrayPool<byte>.Shared.Return(rentedBytes);
            }
        }
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

    // Cap on stack-allocated secret bytes; secrets above this are pooled. Comfortably covers typical
    // secret sizes (16-64 bytes) while bounding stack usage.
    private const int MaxStackSecretBytes = 256;

    private static string Base64UrlEncode(ReadOnlySpan<byte> bytes)
    {
#if NET9_0_OR_GREATER
        return Base64Url.EncodeToString(bytes);
#else
        // base64 encodes n bytes in ceil(n/3)*4 chars; the unpadded base64url form is never longer.
        var maxChars = ((bytes.Length + 2) / 3) * 4;
        Span<char> encoded = stackalloc char[maxChars];
        if (!Convert.TryToBase64Chars(bytes, encoded, out var written))
        {
            // Unreachable: the destination is sized for the worst case.
            throw new InvalidOperationException("Base64 encoding failed.");
        }

        var result = encoded[..written];

        // Map to the URL-safe alphabet and drop padding in place, then materialise once.
        var length = result.Length;
        for (var i = 0; i < length; i++)
        {
            switch (result[i])
            {
                case '+':
                    result[i] = '-';
                    break;
                case '/':
                    result[i] = '_';
                    break;
                case '=':
                    length = i;
                    goto done;
            }
        }

    done:
        return new string(result[..length]);
#endif
    }
}
