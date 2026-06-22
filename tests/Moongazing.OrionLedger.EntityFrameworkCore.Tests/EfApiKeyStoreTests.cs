namespace Moongazing.OrionLedger.EntityFrameworkCore.Tests;

using Microsoft.EntityFrameworkCore;

using Moongazing.OrionLedger.Keys;

/// <summary>
/// EF-store-specific tests that go beyond the shared conformance contract: they assert behaviour
/// that only a real relational engine exhibits (a unique constraint, durability across a fresh
/// context, and a genuinely atomic increment under cross-connection contention).
/// </summary>
public sealed class EfApiKeyStoreTests : IAsyncLifetime
{
    private SqliteApiKeyStoreContext harness = null!;

    public async Task InitializeAsync() => harness = await SqliteApiKeyStoreContext.CreateAsync();

    public async Task DisposeAsync() => await harness.DisposeAsync();

    private EfApiKeyStore<OrionLedgerDbContext> NewStore() => new(harness);

    private static ApiKeyRecord Record(string id, string hash, string? subject = null, IEnumerable<string>? scopes = null) =>
        new()
        {
            Id = id,
            Name = $"name-{id}",
            Subject = subject,
            DisplayPrefix = $"ork_{id}",
            Hash = hash,
            Scopes = scopes is null
                ? new HashSet<string>(StringComparer.Ordinal)
                : new HashSet<string>(scopes, StringComparer.Ordinal),
            CreatedAt = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
        };

    [Fact]
    public async Task the_hash_column_has_a_unique_index()
    {
        var store = NewStore();
        var hash = ApiKeyHasher.Hash("collide");

        await store.AddAsync(Record("first", hash));

        // A second row with the same hash must be rejected by the database, not silently accepted.
        var duplicate = Record("second", hash);
        await Assert.ThrowsAsync<DbUpdateException>(() => store.AddAsync(duplicate));
    }

    [Fact]
    public async Task a_stored_record_is_durable_across_a_fresh_context()
    {
        var store = NewStore();
        var record = Record("durable", ApiKeyHasher.Hash("durable"), subject: "tenant-1", scopes: ["a", "b"]);
        await store.AddAsync(record);

        // Read through a brand-new context with no shared identity map: this proves the row is in the
        // database, not merely in a tracking cache.
        await using var context = harness.CreateDbContext();
        var found = await context.Set<ApiKeyRecord>().AsNoTracking().SingleAsync(r => r.Id == "durable");

        Assert.Equal(record.Hash, found.Hash);
        Assert.Equal("tenant-1", found.Subject);
        Assert.Equal(new SortedSet<string>(["a", "b"], StringComparer.Ordinal),
            new SortedSet<string>(found.Scopes, StringComparer.Ordinal));
    }

    [Fact]
    public async Task scopes_persist_as_a_json_column_value()
    {
        var store = NewStore();
        await store.AddAsync(Record("scoped", ApiKeyHasher.Hash("scoped"), scopes: ["orders:read", "orders:write"]));

        // Inspect the raw stored value to confirm the converter wrote a single JSON column rather
        // than, say, a delimiter-joined string that would break on a scope containing the delimiter.
        await using var context = harness.CreateDbContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT Scopes FROM {ApiKeyRecordConfiguration.DefaultTableName} WHERE Id = 'scoped'";
        var raw = (string?)await command.ExecuteScalarAsync();

        Assert.NotNull(raw);
        Assert.Contains("orders:read", raw, StringComparison.Ordinal);
        Assert.Contains("orders:write", raw, StringComparison.Ordinal);
        Assert.StartsWith("[", raw.TrimStart());
    }

    [Fact]
    public async Task update_of_a_missing_row_throws()
    {
        var store = NewStore();
        var orphan = Record("never-added", ApiKeyHasher.Hash("never-added"));
        orphan.RevokedAt = new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero);

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.UpdateAsync(orphan));
    }

    [Fact]
    public async Task concurrent_increments_are_applied_atomically_at_the_database()
    {
        const int verifications = 100;

        var store = NewStore();
        await store.AddAsync(Record("hot", ApiKeyHasher.Hash("hot")));

        // Each task is one verify: load the record (its own no-tracking instance), bump the counter,
        // persist. The store turns the bump into a server-side `LastUsedCount = LastUsedCount + 1`,
        // so every increment must land even though all tasks contend on the same row.
        var tasks = Enumerable.Range(0, verifications).Select(async _ =>
        {
            var reloaded = await store.FindByIdAsync("hot");
            Assert.NotNull(reloaded);
            reloaded.LastUsedCount += 1;
            reloaded.LastUsedAt = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
            await store.UpdateAsync(reloaded);
        });

        await Task.WhenAll(tasks);

        var final = await store.FindByIdAsync("hot");
        Assert.NotNull(final);
        Assert.Equal(verifications, final.LastUsedCount);
    }

    [Fact]
    public async Task revoke_then_update_does_not_disturb_the_counter()
    {
        var store = NewStore();
        await store.AddAsync(Record("mixed", ApiKeyHasher.Hash("mixed")));

        // Land three verifies.
        for (var i = 0; i < 3; i++)
        {
            var v = await store.FindByIdAsync("mixed");
            Assert.NotNull(v);
            v.LastUsedCount += 1;
            await store.UpdateAsync(v);
        }

        // Now revoke: this update sets RevokedAt and carries the counter at its loaded value, so it
        // must apply a zero delta and leave the count at three.
        var toRevoke = await store.FindByIdAsync("mixed");
        Assert.NotNull(toRevoke);
        toRevoke.RevokedAt = new DateTimeOffset(2026, 6, 3, 0, 0, 0, TimeSpan.Zero);
        await store.UpdateAsync(toRevoke);

        var found = await store.FindByIdAsync("mixed");
        Assert.NotNull(found);
        Assert.Equal(3, found.LastUsedCount);
        Assert.NotNull(found.RevokedAt);
    }

    [Fact]
    public async Task a_stale_last_used_update_does_not_clear_a_concurrent_revocation()
    {
        // The lost-update hazard the store must not have: a verifier loads an active record, another
        // request revokes the same key, then the verifier persists its last-used stamp from the stale
        // snapshot. If the last-used update wrote the whole record it would push RevokedAt = null back
        // over the revocation and resurrect a revoked key. It must instead write only the columns it
        // changed (last-used), leaving the revocation intact.
        var store = NewStore();
        await store.AddAsync(Record("racy", ApiKeyHasher.Hash("racy")));

        // 1. The verifier loads the record while it is still active (its own no-tracking instance).
        var verifierView = await store.FindByIdAsync("racy");
        Assert.NotNull(verifierView);
        Assert.Null(verifierView.RevokedAt);

        // 2. A concurrent request revokes the key through the store on a separate loaded instance.
        var revokerView = await store.FindByIdAsync("racy");
        Assert.NotNull(revokerView);
        var revokedAt = new DateTimeOffset(2026, 6, 4, 0, 0, 0, TimeSpan.Zero);
        revokerView.RevokedAt = revokedAt;
        await store.UpdateAsync(revokerView);

        // 3. The verifier now persists its last-used update from the stale (pre-revocation) snapshot.
        verifierView.LastUsedAt = new DateTimeOffset(2026, 6, 4, 1, 0, 0, TimeSpan.Zero);
        verifierView.LastUsedCount += 1;
        await store.UpdateAsync(verifierView);

        // The revocation must survive: the stale verify did not change RevokedAt, so its update must not
        // have written it. The key stays revoked and the last-used stamp still landed.
        var found = await store.FindByIdAsync("racy");
        Assert.NotNull(found);
        Assert.Equal(revokedAt, found.RevokedAt);
        Assert.Equal(1, found.LastUsedCount);
        Assert.NotNull(found.LastUsedAt);
    }

    [Fact]
    public async Task a_stale_last_used_update_does_not_clear_a_concurrent_rotation()
    {
        // The same hazard for the rotation columns: a stale verify must not blank SupersededAt /
        // SupersededById / RetiresAt that a concurrent rotation set after the record was loaded.
        var store = NewStore();
        await store.AddAsync(Record("rotated", ApiKeyHasher.Hash("rotated")));

        var verifierView = await store.FindByIdAsync("rotated");
        Assert.NotNull(verifierView);

        var rotaterView = await store.FindByIdAsync("rotated");
        Assert.NotNull(rotaterView);
        var supersededAt = new DateTimeOffset(2026, 6, 5, 0, 0, 0, TimeSpan.Zero);
        rotaterView.SupersededAt = supersededAt;
        rotaterView.SupersededById = "successor";
        rotaterView.RetiresAt = supersededAt.AddMinutes(10);
        await store.UpdateAsync(rotaterView);

        // Stale verify persists only last-used.
        verifierView.LastUsedCount += 1;
        await store.UpdateAsync(verifierView);

        var found = await store.FindByIdAsync("rotated");
        Assert.NotNull(found);
        Assert.Equal(supersededAt, found.SupersededAt);
        Assert.Equal("successor", found.SupersededById);
        Assert.Equal(supersededAt.AddMinutes(10), found.RetiresAt);
        Assert.Equal(1, found.LastUsedCount);
    }
}
