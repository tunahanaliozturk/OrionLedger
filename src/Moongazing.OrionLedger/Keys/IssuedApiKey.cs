namespace Moongazing.OrionLedger.Keys;

/// <summary>
/// The result of issuing a key. <see cref="Token"/> is the plaintext key, available only here and
/// never again: show it to the caller once and store nothing but <see cref="Record"/>.
/// </summary>
public sealed class IssuedApiKey
{
    /// <summary>Create an issuance result.</summary>
    /// <param name="token">The plaintext token.</param>
    /// <param name="record">The persisted record.</param>
    public IssuedApiKey(string token, ApiKeyRecord record)
    {
        ArgumentException.ThrowIfNullOrEmpty(token);
        ArgumentNullException.ThrowIfNull(record);
        Token = token;
        Record = record;
    }

    /// <summary>The plaintext key. Surface it to the caller once; it cannot be recovered later.</summary>
    public string Token { get; }

    /// <summary>The stored record (without the plaintext token).</summary>
    public ApiKeyRecord Record { get; }
}
