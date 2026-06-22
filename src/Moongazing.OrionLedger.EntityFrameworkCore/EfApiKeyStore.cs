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
/// <see cref="UpdateAsync"/> writes only the lifecycle columns the caller actually changed, derived
/// per record instance from the values it carried when it was loaded. A verify that stamped only the
/// last-used fields emits a SET clause for those fields alone; it never writes <c>RevokedAt</c> or the
/// rotation columns, so a verify's last-used update cannot resurrect a key a concurrent request
/// revoked by writing a stale <c>RevokedAt = null</c> back over it. Each operation owns its columns:
/// revoke writes <c>RevokedAt</c>, rotation writes the rotation timestamps, verify writes only
/// last-used. The last-used counter is additionally applied as a server-side
/// <c>LastUsedCount = LastUsedCount + delta</c> against the loaded value, so two concurrent
/// verifications each land their <c>+1</c> instead of racing on a read-modify-write.
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

    // The lifecycle state each record carried when it was last loaded (or added) through this store,
    // captured per record instance so UpdateAsync can write back only the columns whose value the
    // caller actually changed. Keyed by the returned record's reference identity: no-tracking reads
    // hand back a distinct object per call, so concurrent loads of the same id get independent
    // snapshots and never race. Entries are collected with their record.
    private readonly ConditionalWeakTable<ApiKeyRecord, LoadedState> loadedState = new();

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

        // The inserted state is the baseline for a later UpdateAsync on this same record instance.
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

        // The state the record was loaded with. A record built fresh (never loaded through this store)
        // has no snapshot, so it is treated as all-default: every field it carries that differs from
        // the default is written, which is the correct behaviour for a first persist of mutations.
        var loaded = LoadedStateOf(record);

        // The last-used counter is applied as a server-side delta from the value the record was loaded
        // with, so concurrent increments compose at the database instead of racing in memory. When the
        // operation did not touch the counter the delta is zero and the (always-present) increment is a
        // harmless no-op.
        var countDelta = record.LastUsedCount - loaded.LastUsedCount;

        // Decide, per lifecycle column, whether this operation changed it relative to the loaded value.
        // A column the caller did not change is set to its own current database value (SET col = col),
        // never to the value the record carries. This is what stops a verify's last-used update from
        // carrying a stale lifecycle field back over a concurrent revoke or rotation: the verify did not
        // change RevokedAt, so the UPDATE re-applies the row's own current RevokedAt within the same
        // atomic statement instead of writing the stale snapshot value (which would clear a revoke that
        // landed after the record was loaded). Each operation thus only ever writes the columns it owns:
        // revoke writes RevokedAt, rotation writes the rotation timestamps, verify writes only last-used.
        //
        // The conditions are captured as constants and the setters are one fluent expression because the
        // EF8/EF9 ExecuteUpdate overload takes an expression tree (the statement-lambda overload is
        // EF10-only); a `flag ? newValue : r.Column` ternary is expression-tree-legal and translates to
        // the column's current value when the flag is false on every supported provider.
        var writeRevokedAt = record.RevokedAt != loaded.RevokedAt;
        var writeLastUsedAt = record.LastUsedAt != loaded.LastUsedAt;
        var writeSupersededAt = record.SupersededAt != loaded.SupersededAt;
        var writeSupersededById = !string.Equals(record.SupersededById, loaded.SupersededById, StringComparison.Ordinal);
        var writeRetiresAt = record.RetiresAt != loaded.RetiresAt;

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var affected = await context.Set<ApiKeyRecord>()
            .Where(r => r.Id == record.Id)
            .ExecuteUpdateAsync(
                setters => setters
                    // The counter is always applied as an atomic server-side increment (zero-delta when
                    // the operation did not touch it), which also guarantees the SET clause is non-empty.
                    .SetProperty(r => r.LastUsedCount, r => r.LastUsedCount + countDelta)
                    .SetProperty(r => r.LastUsedAt, r => writeLastUsedAt ? record.LastUsedAt : r.LastUsedAt)
                    .SetProperty(r => r.RevokedAt, r => writeRevokedAt ? record.RevokedAt : r.RevokedAt)
                    .SetProperty(r => r.SupersededAt, r => writeSupersededAt ? record.SupersededAt : r.SupersededAt)
                    .SetProperty(r => r.SupersededById, r => writeSupersededById ? record.SupersededById : r.SupersededById)
                    .SetProperty(r => r.RetiresAt, r => writeRetiresAt ? record.RetiresAt : r.RetiresAt),
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

        // The committed state now matches what this record carries; rebase its snapshot so a second
        // UpdateAsync for the same instance derives its next delta and column set from the new baseline.
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

        // Ordinal, case-sensitive guard. The subject column is configured with a case-sensitive
        // collation where the provider supports it (see ApiKeyRecordConfiguration), but the database
        // predicate above is still evaluated under the column/connection collation, which on a
        // case-insensitive provider or database would match "Tenant" for "tenant". The store contract
        // requires ordinal matching regardless of provider collation, so re-filter the candidates in
        // memory with an ordinal comparison: a case-variant subject can never be returned (and so can
        // never be bulk-revoked) even on a case-insensitive database. The candidate set is already
        // narrowed by the indexed predicate, so this final pass is over a small list.
        var matches = records
            .Where(r => string.Equals(r.Subject, subject, StringComparison.Ordinal))
            .ToList();

        foreach (var record in matches)
        {
            Remember(record);
        }

        return matches;
    }

    private void Remember(ApiKeyRecord? record)
    {
        if (record is not null)
        {
            loadedState.AddOrUpdate(record, LoadedState.From(record));
        }
    }

    private LoadedState LoadedStateOf(ApiKeyRecord record) =>
        loadedState.TryGetValue(record, out var state) ? state : LoadedState.Default;

    // An immutable snapshot of the mutable lifecycle fields a record carried when it was loaded or
    // added through this store. UpdateAsync diffs the live record against it to decide which columns
    // the caller changed, so each operation writes only the columns it owns.
    private sealed class LoadedState
    {
        // The baseline for a record never seen by this store: all lifecycle fields at their type
        // defaults (counter zero, timestamps and ids null). Diffing against this writes every field the
        // record carries that is not default, which is the correct first persist for a fresh record.
        public static readonly LoadedState Default = new(null, null, 0L, null, null, null);

        private LoadedState(
            DateTimeOffset? revokedAt,
            DateTimeOffset? lastUsedAt,
            long lastUsedCount,
            DateTimeOffset? supersededAt,
            string? supersededById,
            DateTimeOffset? retiresAt)
        {
            RevokedAt = revokedAt;
            LastUsedAt = lastUsedAt;
            LastUsedCount = lastUsedCount;
            SupersededAt = supersededAt;
            SupersededById = supersededById;
            RetiresAt = retiresAt;
        }

        public DateTimeOffset? RevokedAt { get; }

        public DateTimeOffset? LastUsedAt { get; }

        public long LastUsedCount { get; }

        public DateTimeOffset? SupersededAt { get; }

        public string? SupersededById { get; }

        public DateTimeOffset? RetiresAt { get; }

        public static LoadedState From(ApiKeyRecord record) =>
            new(
                record.RevokedAt,
                record.LastUsedAt,
                record.LastUsedCount,
                record.SupersededAt,
                record.SupersededById,
                record.RetiresAt);
    }
}
