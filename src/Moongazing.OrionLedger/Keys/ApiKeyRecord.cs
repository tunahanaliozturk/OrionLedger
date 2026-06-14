namespace Moongazing.OrionLedger.Keys;

/// <summary>
/// The stored representation of an issued key. It never contains the plaintext token, only its
/// hash. A record is mutable in the lifecycle sense (last-used and revoked timestamps change);
/// stores persist those updates through <see cref="Storage.IApiKeyStore.UpdateAsync"/>.
/// </summary>
public sealed class ApiKeyRecord
{
    /// <summary>The stable identifier of this key, assigned at issuance.</summary>
    public required string Id { get; init; }

    /// <summary>A human-recognisable label supplied at issuance (for example a tenant or app name).</summary>
    public required string Name { get; init; }

    /// <summary>The non-secret display prefix of the token (for example <c>ork_AbCd12Ef</c>).</summary>
    public required string DisplayPrefix { get; init; }

    /// <summary>The SHA-256 hash of the token. The lookup key for verification.</summary>
    public required string Hash { get; init; }

    /// <summary>The scopes granted to this key.</summary>
    public required IReadOnlySet<string> Scopes { get; init; }

    /// <summary>When the key was issued.</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>When the key expires, or null if it never expires.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>When the key was revoked, or null if it is still active.</summary>
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>When the key was last successfully verified, or null if never used.</summary>
    public DateTimeOffset? LastUsedAt { get; set; }
}
