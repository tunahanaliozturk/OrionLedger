namespace Moongazing.OrionLedger.Conformance;

using System.Globalization;

using Moongazing.OrionLedger.Keys;
using Moongazing.OrionLedger.Storage;

using Xunit;

// xUnit fact names read as sentences with underscores; CA1707 (no underscores in identifiers) is the
// wrong rule for a test suite, so it is suppressed for this file only rather than project wide.
#pragma warning disable CA1707

/// <summary>
/// A reusable contract test suite for <see cref="IApiKeyStore"/>. Derive a concrete test class,
/// implement <see cref="CreateStoreAsync"/> to return a fresh, empty store for each test, and the
/// inherited facts verify that the implementation honours the store contract: add, lookup by hash
/// and id, update of the mutable lifecycle fields, revocation, rotation timestamps, last-used
/// tracking (including that concurrent increments are not lost), scope round-trips, and lookup by
/// subject.
/// </summary>
/// <remarks>
/// xUnit constructs the test class once per fact, so a store returned from
/// <see cref="CreateStoreAsync"/> is isolated to a single test. Override
/// <see cref="DisposeStoreAsync"/> to release per-test resources (for example a database connection
/// or a temporary file).
/// </remarks>
public abstract class ApiKeyStoreConformanceTests : IAsyncLifetime
{
    private IApiKeyStore store = null!;

    /// <summary>
    /// Create a fresh, empty <see cref="IApiKeyStore"/> for a single test. Called once per fact by
    /// <see cref="InitializeAsync"/>.
    /// </summary>
    /// <returns>A new store with no records.</returns>
    protected abstract Task<IApiKeyStore> CreateStoreAsync();

    /// <summary>
    /// Release any resources created for the test. The default does nothing; override it to dispose a
    /// connection, drop a schema, or delete a temporary database.
    /// </summary>
    /// <param name="store">The store created for the test.</param>
    /// <returns>A task that completes when teardown is done.</returns>
    protected virtual Task DisposeStoreAsync(IApiKeyStore store) => Task.CompletedTask;

    /// <summary>The store under test for the current fact.</summary>
    protected IApiKeyStore Store => store;

    /// <inheritdoc />
    public async Task InitializeAsync() => store = await CreateStoreAsync();

    /// <inheritdoc />
    public async Task DisposeAsync() => await DisposeStoreAsync(store);

    /// <summary>A stored record is retrievable by its hash.</summary>
    [Fact]
    public async Task add_then_find_by_hash_returns_the_record()
    {
        var record = NewRecord();
        await Store.AddAsync(record);

        var found = await Store.FindByHashAsync(record.Hash);

        Assert.NotNull(found);
        Assert.Equal(record.Id, found.Id);
        Assert.Equal(record.Hash, found.Hash);
        Assert.Equal(record.Name, found.Name);
    }

    /// <summary>A hash that was never stored resolves to null, not an error.</summary>
    [Fact]
    public async Task find_by_hash_miss_returns_null()
    {
        await Store.AddAsync(NewRecord());

        var found = await Store.FindByHashAsync(Hash("absent"));

        Assert.Null(found);
    }

    /// <summary>A stored record is retrievable by its id.</summary>
    [Fact]
    public async Task add_then_find_by_id_returns_the_record()
    {
        var record = NewRecord();
        await Store.AddAsync(record);

        var found = await Store.FindByIdAsync(record.Id);

        Assert.NotNull(found);
        Assert.Equal(record.Id, found.Id);
    }

    /// <summary>An id that was never stored resolves to null.</summary>
    [Fact]
    public async Task find_by_id_miss_returns_null()
    {
        var found = await Store.FindByIdAsync("does-not-exist");

        Assert.Null(found);
    }

    /// <summary>The granted scopes survive a store round-trip exactly, order independent.</summary>
    [Fact]
    public async Task scopes_round_trip_through_the_store()
    {
        var record = NewRecord(scopes: ["orders:read", "orders:write", "reports:read"]);
        await Store.AddAsync(record);

        var found = await Store.FindByHashAsync(record.Hash);

        Assert.NotNull(found);
        Assert.Equal(
            new SortedSet<string>(record.Scopes, StringComparer.Ordinal),
            new SortedSet<string>(found.Scopes, StringComparer.Ordinal));
    }

    /// <summary>A key issued with no scopes round-trips as an empty set, not null.</summary>
    [Fact]
    public async Task empty_scopes_round_trip_as_an_empty_set()
    {
        var record = NewRecord(scopes: []);
        await Store.AddAsync(record);

        var found = await Store.FindByHashAsync(record.Hash);

        Assert.NotNull(found);
        Assert.NotNull(found.Scopes);
        Assert.Empty(found.Scopes);
    }

    /// <summary>Setting the revoked timestamp through update is persisted.</summary>
    [Fact]
    public async Task update_persists_revocation()
    {
        var record = NewRecord();
        await Store.AddAsync(record);

        var revokedAt = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        record.RevokedAt = revokedAt;
        await Store.UpdateAsync(record);

        var found = await Store.FindByIdAsync(record.Id);

        Assert.NotNull(found);
        Assert.Equal(revokedAt, found.RevokedAt);
    }

    /// <summary>The rotation timestamps and successor id are persisted through update.</summary>
    [Fact]
    public async Task update_persists_rotation_timestamps()
    {
        var record = NewRecord();
        await Store.AddAsync(record);

        var supersededAt = new DateTimeOffset(2026, 5, 6, 7, 8, 9, TimeSpan.Zero);
        var retiresAt = supersededAt.AddMinutes(5);
        record.SupersededAt = supersededAt;
        record.SupersededById = "successor-key-id";
        record.RetiresAt = retiresAt;
        await Store.UpdateAsync(record);

        var found = await Store.FindByIdAsync(record.Id);

        Assert.NotNull(found);
        Assert.Equal(supersededAt, found.SupersededAt);
        Assert.Equal("successor-key-id", found.SupersededById);
        Assert.Equal(retiresAt, found.RetiresAt);
    }

    /// <summary>A single last-used update stamps the timestamp and advances the counter.</summary>
    [Fact]
    public async Task update_persists_last_used_stamp_and_count()
    {
        var record = NewRecord();
        await Store.AddAsync(record);

        var usedAt = new DateTimeOffset(2026, 7, 8, 9, 10, 11, TimeSpan.Zero);
        record.LastUsedAt = usedAt;
        record.LastUsedCount += 1;
        await Store.UpdateAsync(record);

        var found = await Store.FindByIdAsync(record.Id);

        Assert.NotNull(found);
        Assert.Equal(usedAt, found.LastUsedAt);
        Assert.Equal(1, found.LastUsedCount);
    }

    /// <summary>
    /// Sequential last-used updates accumulate the counter rather than overwriting it, mirroring the
    /// per-verify increment the service performs.
    /// </summary>
    [Fact]
    public async Task sequential_last_used_updates_accumulate_the_count()
    {
        var record = NewRecord();
        await Store.AddAsync(record);

        for (var i = 0; i < 5; i++)
        {
            var reloaded = await Store.FindByIdAsync(record.Id);
            Assert.NotNull(reloaded);
            reloaded.LastUsedAt = BaseTime.AddSeconds(i);
            reloaded.LastUsedCount += 1;
            await Store.UpdateAsync(reloaded);
        }

        var found = await Store.FindByIdAsync(record.Id);

        Assert.NotNull(found);
        Assert.Equal(5, found.LastUsedCount);
    }

    /// <summary>
    /// Concurrent last-used updates do not lose counts. Each task mirrors one verify: load the
    /// record, stamp it, increment the counter by one, and persist. A store that performs the
    /// increment atomically lands every one; a naive read-modify-write store would lose some.
    /// </summary>
    [Fact]
    public async Task concurrent_last_used_updates_do_not_lose_counts()
    {
        const int verifications = 50;

        var record = NewRecord();
        await Store.AddAsync(record);

        var tasks = Enumerable.Range(0, verifications).Select(async _ =>
        {
            var reloaded = await Store.FindByIdAsync(record.Id);
            Assert.NotNull(reloaded);
            reloaded.LastUsedAt = BaseTime;
            reloaded.LastUsedCount += 1;
            await Store.UpdateAsync(reloaded);
        });

        await Task.WhenAll(tasks);

        var found = await Store.FindByIdAsync(record.Id);

        Assert.NotNull(found);
        Assert.Equal(verifications, found.LastUsedCount);
    }

    /// <summary>Every record for a subject is returned, including already revoked ones.</summary>
    [Fact]
    public async Task find_by_subject_returns_all_records_for_that_subject()
    {
        var first = NewRecord(subject: "tenant-1");
        var second = NewRecord(subject: "tenant-1");
        var revoked = NewRecord(subject: "tenant-1");
        revoked.RevokedAt = BaseTime;
        var other = NewRecord(subject: "tenant-2");
        var none = NewRecord(subject: null);

        await Store.AddAsync(first);
        await Store.AddAsync(second);
        await Store.AddAsync(revoked);
        await Store.AddAsync(other);
        await Store.AddAsync(none);

        var matches = await Store.FindBySubjectAsync("tenant-1");

        Assert.Equal(
            new[] { first.Id, second.Id, revoked.Id }.OrderBy(x => x, StringComparer.Ordinal),
            matches.Select(m => m.Id).OrderBy(x => x, StringComparer.Ordinal));
    }

    /// <summary>Lookup by subject matches ordinally and case sensitively.</summary>
    [Fact]
    public async Task find_by_subject_is_case_sensitive()
    {
        var record = NewRecord(subject: "Tenant");
        await Store.AddAsync(record);

        var miss = await Store.FindBySubjectAsync("tenant");
        var hit = await Store.FindBySubjectAsync("Tenant");

        Assert.Empty(miss);
        Assert.Single(hit);
    }

    /// <summary>A subject with no keys returns an empty list, not null and not an error.</summary>
    [Fact]
    public async Task find_by_subject_with_no_matches_returns_empty()
    {
        await Store.AddAsync(NewRecord(subject: "tenant-1"));

        var matches = await Store.FindBySubjectAsync("tenant-absent");

        Assert.NotNull(matches);
        Assert.Empty(matches);
    }

    /// <summary>An empty subject matches nothing: keys without a subject are not bulk addressable.</summary>
    [Fact]
    public async Task find_by_subject_with_empty_argument_returns_empty()
    {
        await Store.AddAsync(NewRecord(subject: null));

        var matches = await Store.FindBySubjectAsync(string.Empty);

        Assert.NotNull(matches);
        Assert.Empty(matches);
    }

    /// <summary>The instant all relative-time fields in a test are anchored to.</summary>
    protected static DateTimeOffset BaseTime { get; } =
        new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

    private int sequence;

    /// <summary>
    /// Build a valid <see cref="ApiKeyRecord"/> with a unique id and hash. Each call produces a
    /// distinct record so a single test can store several without colliding on the unique hash.
    /// </summary>
    /// <param name="subject">The owner to assign, or null for none.</param>
    /// <param name="scopes">The scopes to grant, or null for an empty set.</param>
    /// <returns>A fresh record ready to store.</returns>
    protected ApiKeyRecord NewRecord(string? subject = null, IEnumerable<string>? scopes = null)
    {
        var n = Interlocked.Increment(ref sequence);
        var token = string.Create(CultureInfo.InvariantCulture, $"key-{n}");
        return new ApiKeyRecord
        {
            Id = string.Create(CultureInfo.InvariantCulture, $"id-{n}"),
            Name = string.Create(CultureInfo.InvariantCulture, $"name-{n}"),
            Subject = subject,
            DisplayPrefix = string.Create(CultureInfo.InvariantCulture, $"ork_{n:D4}"),
            Hash = Hash(token),
            Scopes = scopes is null
                ? new HashSet<string>(StringComparer.Ordinal)
                : new HashSet<string>(scopes, StringComparer.Ordinal),
            CreatedAt = BaseTime,
        };
    }

    /// <summary>The SHA-256 hash of a token, in the 64-character lowercase hex form the store expects.</summary>
    /// <param name="token">The token to hash.</param>
    /// <returns>The lowercase hex digest.</returns>
    protected static string Hash(string token) => ApiKeyHasher.Hash(token);
}

#pragma warning restore CA1707
