namespace Moongazing.OrionLedger;

using Moongazing.OrionLedger.Keys;

/// <summary>
/// The key lifecycle entry point: issue keys, verify presented tokens, and revoke keys.
/// </summary>
public interface IApiKeyService
{
    /// <summary>
    /// Issue a new key. The plaintext token is returned once on <see cref="IssuedApiKey.Token"/>
    /// and never stored, so surface it to the caller immediately.
    /// </summary>
    /// <param name="name">A human-recognisable label (for example a tenant or app name).</param>
    /// <param name="scopes">The scopes to grant, or null for none.</param>
    /// <param name="expiresAt">
    /// An explicit expiry. When null, the configured default lifetime applies (and if that is also
    /// unset, the key does not expire).
    /// </param>
    /// <param name="subject">
    /// The owner this key belongs to (for example a user, tenant, or service id), or null for none.
    /// Keys sharing a subject can be revoked together with <see cref="RevokeAllForSubjectAsync"/>.
    /// </param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<IssuedApiKey> IssueAsync(
        string name,
        IEnumerable<string>? scopes = null,
        DateTimeOffset? expiresAt = null,
        string? subject = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verify a presented token. On success the matched key's last-used timestamp is updated.
    /// </summary>
    /// <param name="token">The plaintext token presented by the caller.</param>
    /// <param name="requiredScope">
    /// A scope the key must hold, or null to skip the scope check.
    /// </param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<ApiKeyVerification> VerifyAsync(
        string? token,
        string? requiredScope = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Revoke a key by id. Returns true if an active key was revoked, false if no such key exists
    /// or it was already revoked.
    /// </summary>
    /// <param name="id">The key id.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<bool> RevokeAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rotate a key: issue a fresh successor key (new id, secret, and hash) that inherits the
    /// predecessor's name, subject, scopes, and expiry, then supersede the predecessor. The new
    /// plaintext token is returned once on <see cref="KeyRotation.Token"/> and never again.
    /// </summary>
    /// <remarks>
    /// With a positive <paramref name="grace"/>, the predecessor keeps verifying for that window and
    /// then resolves as <see cref="ApiKeyStatus.Retired"/>, so callers presenting the old token have
    /// time to migrate. With a null or zero grace the predecessor is revoked immediately. Rotating a
    /// key that is missing, revoked, expired, or already superseded returns null.
    /// </remarks>
    /// <param name="id">The id of the key to rotate.</param>
    /// <param name="grace">
    /// How long the old key keeps verifying before it retires. Null or <see cref="TimeSpan.Zero"/>
    /// retires it immediately (revoked). Must not be negative.
    /// </param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The rotation result with the new token, or null if the key could not be rotated.</returns>
    Task<KeyRotation?> RotateAsync(
        string id,
        TimeSpan? grace = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Revoke every active key belonging to a subject in one call. Already revoked, expired, or
    /// retired keys are skipped. Returns the number of keys that were newly revoked.
    /// </summary>
    /// <remarks>
    /// Requires the store to support lookup by subject
    /// (<see cref="Storage.IApiKeyStore.FindBySubjectAsync"/>); the in-memory store does. A store
    /// that does not throws <see cref="NotSupportedException"/>.
    /// </remarks>
    /// <param name="subject">The owner whose keys should be revoked.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The count of keys newly revoked by this call.</returns>
    Task<int> RevokeAllForSubjectAsync(string subject, CancellationToken cancellationToken = default);
}
