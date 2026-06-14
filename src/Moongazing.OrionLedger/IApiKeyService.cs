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
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<IssuedApiKey> IssueAsync(
        string name,
        IEnumerable<string>? scopes = null,
        DateTimeOffset? expiresAt = null,
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
}
