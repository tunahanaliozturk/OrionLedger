namespace Moongazing.OrionLedger.Storage;

using System.Collections.Concurrent;

using Moongazing.OrionLedger.Keys;

/// <summary>
/// A process-local <see cref="IApiKeyStore"/> backed by concurrent dictionaries indexed by hash
/// and by id. Suitable for a single instance or tests; use a database-backed store for a
/// multi-instance deployment or to survive restarts.
/// </summary>
public sealed class InMemoryApiKeyStore : IApiKeyStore
{
    private readonly ConcurrentDictionary<string, ApiKeyRecord> byHash = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ApiKeyRecord> byId = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task AddAsync(ApiKeyRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        byHash[record.Hash] = record;
        byId[record.Id] = record;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<ApiKeyRecord?> FindByHashAsync(string hash, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(hash);
        return Task.FromResult(byHash.GetValueOrDefault(hash));
    }

    /// <inheritdoc />
    public Task<ApiKeyRecord?> FindByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        return Task.FromResult(byId.GetValueOrDefault(id));
    }

    /// <inheritdoc />
    public Task UpdateAsync(ApiKeyRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        // Records are reference types shared between both indexes, so in-place mutation is already
        // visible; re-seat them anyway so a store swapped for a real one keeps the same contract.
        byHash[record.Hash] = record;
        byId[record.Id] = record;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ApiKeyRecord>> FindBySubjectAsync(
        string subject,
        CancellationToken cancellationToken = default)
    {
        // A null or empty subject matches nothing: keys issued without a subject are not addressable
        // in bulk, so they cannot be swept by an empty argument.
        if (string.IsNullOrEmpty(subject))
        {
            return Task.FromResult<IReadOnlyList<ApiKeyRecord>>([]);
        }

        // Snapshot the id index rather than maintaining a separate subject index: subject is a
        // lifecycle-immutable init-only property, but a flat scan keeps the store simple and the
        // record count in a process-local store is small. A database-backed store would index this.
        var matches = byId.Values
            .Where(r => string.Equals(r.Subject, subject, StringComparison.Ordinal))
            .ToList();
        return Task.FromResult<IReadOnlyList<ApiKeyRecord>>(matches);
    }
}
