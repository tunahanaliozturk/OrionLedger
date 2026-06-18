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

    /// <summary>
    /// The owner or subject this key belongs to (for example a user id, tenant id, or service
    /// principal), or null if none was supplied. Keys are grouped by subject for bulk operations
    /// such as <see cref="IApiKeyService.RevokeAllForSubjectAsync"/>.
    /// </summary>
    public string? Subject { get; init; }

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

    /// <summary>
    /// How many times this key has been successfully verified. Incremented on each valid
    /// verification alongside <see cref="LastUsedAt"/>. Zero until first use.
    /// </summary>
    public long LastUsedCount { get; set; }

    /// <summary>
    /// When this key was superseded by a rotation, or null if it has not been rotated. Set together
    /// with <see cref="SupersededById"/> and (when a grace window was requested) <see cref="RetiresAt"/>.
    /// </summary>
    public DateTimeOffset? SupersededAt { get; set; }

    /// <summary>
    /// The id of the successor key issued when this key was rotated, or null if it has not been
    /// rotated. The successor is an independent key with its own secret.
    /// </summary>
    public string? SupersededById { get; set; }

    /// <summary>
    /// When a superseded key stops verifying. During rotation with a grace window, the old key keeps
    /// verifying until this instant and resolves as retired at or after it. Null when the key was not
    /// rotated, or was rotated with no grace (in which case it is revoked immediately instead).
    /// </summary>
    public DateTimeOffset? RetiresAt { get; set; }
}
