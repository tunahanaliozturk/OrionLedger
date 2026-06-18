namespace Moongazing.OrionLedger.Tests;

using Moongazing.OrionLedger.Keys;
using Moongazing.OrionLedger.Storage;

using Xunit;

/// <summary>
/// Direct coverage of the default <see cref="InMemoryApiKeyStore"/>: the hash and id indexes, miss
/// behaviour, update visibility, and argument guards. This is the contract a database-backed store
/// must also honour.
/// </summary>
public sealed class InMemoryApiKeyStoreTests
{
    private static ApiKeyRecord NewRecord(string id, string hash) => new()
    {
        Id = id,
        Name = "acme",
        DisplayPrefix = "ork_display",
        Hash = hash,
        Scopes = new HashSet<string>(StringComparer.Ordinal),
        CreatedAt = DateTimeOffset.UnixEpoch,
    };

    [Fact]
    public async Task Added_records_are_found_by_hash_and_by_id()
    {
        var store = new InMemoryApiKeyStore();
        var record = NewRecord("id-1", "hash-1");
        await store.AddAsync(record);

        Assert.Same(record, await store.FindByHashAsync("hash-1"));
        Assert.Same(record, await store.FindByIdAsync("id-1"));
    }

    [Fact]
    public async Task An_unknown_hash_or_id_returns_null()
    {
        var store = new InMemoryApiKeyStore();
        Assert.Null(await store.FindByHashAsync("nope"));
        Assert.Null(await store.FindByIdAsync("nope"));
    }

    [Fact]
    public async Task Lookups_are_ordinal_case_sensitive()
    {
        var store = new InMemoryApiKeyStore();
        await store.AddAsync(NewRecord("id-1", "hash-1"));

        Assert.Null(await store.FindByHashAsync("HASH-1"));
        Assert.Null(await store.FindByIdAsync("ID-1"));
    }

    [Fact]
    public async Task Update_persists_mutated_timestamps()
    {
        var store = new InMemoryApiKeyStore();
        var record = NewRecord("id-1", "hash-1");
        await store.AddAsync(record);

        record.RevokedAt = DateTimeOffset.UnixEpoch.AddDays(1);
        record.LastUsedAt = DateTimeOffset.UnixEpoch.AddHours(1);
        await store.UpdateAsync(record);

        var reloaded = await store.FindByIdAsync("id-1");
        Assert.Equal(record.RevokedAt, reloaded!.RevokedAt);
        Assert.Equal(record.LastUsedAt, reloaded.LastUsedAt);
    }

    [Fact]
    public async Task Multiple_records_coexist_under_distinct_keys()
    {
        var store = new InMemoryApiKeyStore();
        await store.AddAsync(NewRecord("id-1", "hash-1"));
        await store.AddAsync(NewRecord("id-2", "hash-2"));

        Assert.Equal("id-1", (await store.FindByHashAsync("hash-1"))!.Id);
        Assert.Equal("id-2", (await store.FindByHashAsync("hash-2"))!.Id);
    }

    [Fact]
    public async Task Add_rejects_a_null_record()
    {
        var store = new InMemoryApiKeyStore();
        await Assert.ThrowsAsync<ArgumentNullException>(() => store.AddAsync(null!));
    }

    [Fact]
    public async Task Update_rejects_a_null_record()
    {
        var store = new InMemoryApiKeyStore();
        await Assert.ThrowsAsync<ArgumentNullException>(() => store.UpdateAsync(null!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task FindByHash_rejects_a_null_or_empty_hash(string? hash)
    {
        var store = new InMemoryApiKeyStore();
        // ThrowIfNullOrEmpty raises ArgumentNullException for null, ArgumentException for empty.
        await Assert.ThrowsAnyAsync<ArgumentException>(() => store.FindByHashAsync(hash!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task FindById_rejects_a_null_or_empty_id(string? id)
    {
        var store = new InMemoryApiKeyStore();
        await Assert.ThrowsAnyAsync<ArgumentException>(() => store.FindByIdAsync(id!));
    }
}
