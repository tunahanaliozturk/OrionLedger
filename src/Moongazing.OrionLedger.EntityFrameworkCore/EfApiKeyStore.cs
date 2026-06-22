namespace Moongazing.OrionLedger.EntityFrameworkCore;

using System.Runtime.CompilerServices;

using Microsoft.EntityFrameworkCore;

using Moongazing.OrionLedger.Keys;
using Moongazing.OrionLedger.Storage;

/// <summary>
/// An <see cref="IApiKeyStore"/> backed by Entity Framework Core. Lookups by hash and by id are
/// index seeks; mutations are pushed to the database as a single atomic relational update so a
/// concurrent verify and a revoke do not interleave into a lost update.
/// </summary>
/// <remarks>
/// <para>
/// The store resolves a fresh <typeparamref name="TContext"/> per operation from an
/// <see cref="IDbContextFactory{TContext}"/>, so a single store instance is safe to use from
/// concurrent verifications (each call owns its context; a <see cref="DbContext"/> is not
/// thread-safe). Register it with
/// <see cref="OrionLedgerEntityFrameworkCoreServiceCollectionExtensions.AddOrionLedgerEntityFrameworkCoreStore{TContext}(Microsoft.Extensions.DependencyInjection.IServiceCollection, System.Action{Microsoft.EntityFrameworkCore.DbContextOptionsBuilder})"/>,
/// which also registers the pooling context factory.
/// </para>
/// <para>
/// The last-used counter is the one field that must survive concurrent writers, so
/// <see cref="UpdateAsync"/> applies it as a server-side <c>LastUsedCount = LastUsedCount + delta</c>
/// rather than writing back a value read into memory. The delta is taken against the value the
/// record carried when it was loaded, captured per returned record instance, so two concurrent
/// verifications each land their <c>+1</c> instead of racing on a read-modify-write. The remaining
/// lifecycle fields (revocation and rotation timestamps) are last-writer-wins, which matches their
/// idempotent intent.
/// </para>
/// <para>
/// The backing <typeparamref name="TContext"/> must map <see cref="ApiKeyRecord"/>; the bundled
/// <see cref="OrionLedgerDbContext"/> does, or apply <see cref="ApiKeyRecordConfiguration"/> to your
/// own context.
/// </para>
/// </remarks>
/// <typeparam name="TContext">The context type that maps <see cref="ApiKeyRecord"/>.</typeparam>
public sealed class EfApiKeyStore<TContext> : IApiKeyStore
    where TContext : DbContext
{
    private readonly IDbContextFactory<TContext> contextFactory;

    // The last-used counter as it was loaded, captured per record instance so UpdateAsync can derive
    // the intended delta and apply it server side. Keyed by the returned record's reference identity:
    // no-tracking reads hand back a distinct object per call, so concurrent loads of the same id get
    // independent snapshots and never race. Entries are collected with their record.
    private readonly ConditionalWeakTable<ApiKeyRecord, StrongBox<long>> loadedCounts = new();

    /// <summary>Create the store over a context factory.</summary>
    /// <param name="contextFactory">A factory for the backing context. Must map <see cref="ApiKeyRecord"/>.</param>
    public EfApiKeyStore(IDbContextFactory<TContext> contextFactory)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        this.contextFactory = contextFactory;
    }

    /// <inheritdoc />
    public async Task AddAsync(ApiKeyRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        context.Set<ApiKeyRecord>().Add(record);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // The inserted counter is the baseline for a later UpdateAsync on this same record instance.
        Remember(record);
    }

    /// <inheritdoc />
    public async Task<ApiKeyRecord?> FindByHashAsync(string hash, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(hash);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var record = await context.Set<ApiKeyRecord>()
            .AsNoTracking()
            .SingleOrDefaultAsync(r => r.Hash == hash, cancellationToken)
            .ConfigureAwait(false);

        Remember(record);
        return record;
    }

    /// <inheritdoc />
    public async Task<ApiKeyRecord?> FindByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var record = await context.Set<ApiKeyRecord>()
            .AsNoTracking()
            .SingleOrDefaultAsync(r => r.Id == id, cancellationToken)
            .ConfigureAwait(false);

        Remember(record);
        return record;
    }

    /// <inheritdoc />
    public async Task UpdateAsync(ApiKeyRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        // The counter is applied as a server-side delta from the value the record was loaded with, so
        // concurrent increments compose at the database instead of racing in memory. A record that
        // was built fresh (never loaded through this store) has no snapshot, so its delta is its full
        // current count, which is the correct behaviour for a counter that started at zero.
        var delta = record.LastUsedCount - LoadedCount(record);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var affected = await context.Set<ApiKeyRecord>()
            .Where(r => r.Id == record.Id)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(r => r.RevokedAt, record.RevokedAt)
                    .SetProperty(r => r.LastUsedAt, record.LastUsedAt)
                    .SetProperty(r => r.LastUsedCount, r => r.LastUsedCount + delta)
                    .SetProperty(r => r.SupersededAt, record.SupersededAt)
                    .SetProperty(r => r.SupersededById, record.SupersededById)
                    .SetProperty(r => r.RetiresAt, record.RetiresAt),
                cancellationToken)
            .ConfigureAwait(false);

        if (affected == 0)
        {
            // No row matched the id: the record was never added, or was deleted out from under us.
            // Surface it rather than silently no-op, mirroring how a relational UPDATE of a missing
            // row is a caller error in this store's contract.
            throw new InvalidOperationException(
                $"Cannot update API key record '{record.Id}': no row with that id exists.");
        }

        // The committed counter has advanced by delta; rebase this record's snapshot so a second
        // UpdateAsync for the same instance derives its next delta from the new baseline.
        Remember(record);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ApiKeyRecord>> FindBySubjectAsync(
        string subject,
        CancellationToken cancellationToken = default)
    {
        // A null or empty subject matches nothing: keys issued without a subject are not addressable
        // in bulk, so they cannot be swept by an empty argument. Matching the in-memory store.
        if (string.IsNullOrEmpty(subject))
        {
            return [];
        }

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var records = await context.Set<ApiKeyRecord>()
            .AsNoTracking()
            .Where(r => r.Subject == subject)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var record in records)
        {
            Remember(record);
        }

        return records;
    }

    private void Remember(ApiKeyRecord? record)
    {
        if (record is not null)
        {
            loadedCounts.AddOrUpdate(record, new StrongBox<long>(record.LastUsedCount));
        }
    }

    private long LoadedCount(ApiKeyRecord record) =>
        loadedCounts.TryGetValue(record, out var box) ? box.Value : 0L;
}
