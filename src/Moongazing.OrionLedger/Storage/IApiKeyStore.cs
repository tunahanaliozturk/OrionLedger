namespace Moongazing.OrionLedger.Storage;

using Moongazing.OrionLedger.Keys;

/// <summary>
/// Persists API key records. The default <see cref="InMemoryApiKeyStore"/> is process-local;
/// implement this interface over a database to share keys across instances and survive restarts.
/// Lookups are by token hash, which is the verification path, and by id for administration.
/// </summary>
public interface IApiKeyStore
{
    /// <summary>Persist a newly issued record.</summary>
    /// <param name="record">The record to store.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task AddAsync(ApiKeyRecord record, CancellationToken cancellationToken = default);

    /// <summary>Find the record whose token hash matches, or null if none.</summary>
    /// <param name="hash">The SHA-256 hash of the presented token.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<ApiKeyRecord?> FindByHashAsync(string hash, CancellationToken cancellationToken = default);

    /// <summary>Find the record with this id, or null if none.</summary>
    /// <param name="id">The key id.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<ApiKeyRecord?> FindByIdAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Persist mutations to an existing record (last-used, revoked, and rotation timestamps).</summary>
    /// <param name="record">The record to update.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task UpdateAsync(ApiKeyRecord record, CancellationToken cancellationToken = default);

    /// <summary>
    /// Return every record whose <see cref="ApiKeyRecord.Subject"/> matches, including already
    /// revoked, expired, or retired keys. Used for bulk revocation by owner.
    /// </summary>
    /// <remarks>
    /// This is a default interface method so existing stores written against 0.1.0 keep compiling.
    /// The default throws <see cref="NotSupportedException"/>: a store must override it to support
    /// <see cref="IApiKeyService.RevokeAllForSubjectAsync"/>. The built-in
    /// <see cref="InMemoryApiKeyStore"/> implements it. Subject matching is ordinal and case
    /// sensitive; a null or empty subject yields an empty result.
    /// </remarks>
    /// <param name="subject">The owner or subject to match.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task<IReadOnlyList<ApiKeyRecord>> FindBySubjectAsync(
        string subject,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            $"{GetType().Name} does not support lookup by subject. Override " +
            $"{nameof(IApiKeyStore)}.{nameof(FindBySubjectAsync)} to enable bulk revocation by owner.");
}
