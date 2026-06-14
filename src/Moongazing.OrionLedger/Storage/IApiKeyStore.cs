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

    /// <summary>Persist mutations to an existing record (last-used and revoked timestamps).</summary>
    /// <param name="record">The record to update.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task UpdateAsync(ApiKeyRecord record, CancellationToken cancellationToken = default);
}
