namespace Moongazing.OrionLedger.EntityFrameworkCore.Tests;

using System.Globalization;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

using Moongazing.OrionLedger.Keys;

// xUnit fact names read as sentences with underscores; CA1707 (no underscores in identifiers) is the
// wrong rule for a test suite. The project suppresses it globally; restate it here for clarity.
#pragma warning disable CA1707

/// <summary>
/// Proves that <see cref="EfApiKeyStore{TContext}"/> matches subjects ordinally and case sensitively
/// even when the underlying database collates case insensitively. The shared conformance suite runs
/// on SQLite's default (case-sensitive for ASCII) collation, so it cannot by itself catch a store
/// that leans on the provider for case sensitivity. This test forces the subject column to
/// <c>COLLATE NOCASE</c>, which makes SQLite behave like a case-insensitive provider/database, and
/// asserts the store's own ordinal guard still refuses to match or revoke a case-variant subject.
/// </summary>
public sealed class EfOrdinalSubjectTests : IAsyncLifetime
{
    private CaseInsensitiveSubjectContextFactory harness = null!;

    /// <inheritdoc />
    public async Task InitializeAsync() => harness = await CaseInsensitiveSubjectContextFactory.CreateAsync();

    /// <inheritdoc />
    public async Task DisposeAsync() => await harness.DisposeAsync();

    private EfApiKeyStore<CaseInsensitiveSubjectContext> NewStore() => new(harness);

    private static ApiKeyRecord Record(string id, string subject) =>
        new()
        {
            Id = id,
            Name = $"name-{id}",
            Subject = subject,
            DisplayPrefix = $"ork_{id}",
            Hash = ApiKeyHasher.Hash(id),
            Scopes = new HashSet<string>(StringComparer.Ordinal),
            CreatedAt = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
        };

    [Fact]
    public async Task the_database_really_is_case_insensitive_for_the_subject_column()
    {
        // Guard the guard: confirm the NOCASE collation is in effect, so the ordinal assertions below
        // are meaningful. A raw case-insensitive LIKE/= against the column matches across case here.
        var store = NewStore();
        await store.AddAsync(Record("probe", subject: "Tenant"));

        await using var context = harness.CreateDbContext();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT COUNT(*) FROM {ApiKeyRecordConfiguration.DefaultTableName} WHERE Subject = 'tenant'";
        var rawMatches = (long)(await command.ExecuteScalarAsync())!;

        // The database itself matches "tenant" against the stored "Tenant": this is a case-insensitive DB.
        Assert.Equal(1, rawMatches);
    }

    [Fact]
    public async Task find_by_subject_does_not_match_a_case_variant_on_a_case_insensitive_database()
    {
        var store = NewStore();
        await store.AddAsync(Record("k1", subject: "Tenant"));

        // The database would match "tenant" (proven above), but the store's ordinal guard must not.
        var miss = await store.FindBySubjectAsync("tenant");
        var hit = await store.FindBySubjectAsync("Tenant");

        Assert.Empty(miss);
        Assert.Single(hit);
        Assert.Equal("k1", hit[0].Id);
    }

    [Fact]
    public async Task bulk_revoke_by_subject_does_not_revoke_a_case_variant_on_a_case_insensitive_database()
    {
        // The end-to-end consequence of the bug: RevokeAllForSubjectAsync("tenant") must not revoke a
        // key stored under "Tenant". The store feeds the service's bulk revoke through FindBySubjectAsync,
        // so an ordinal miss there means the case-variant key is never handed to the revoke loop.
        var store = NewStore();
        await store.AddAsync(Record("upper", subject: "Tenant"));
        await store.AddAsync(Record("lower", subject: "tenant"));

        // Simulate the service's bulk revoke for the lowercase subject: only exact-case matches return.
        var toRevoke = await store.FindBySubjectAsync("tenant");

        Assert.Single(toRevoke);
        Assert.Equal("lower", toRevoke[0].Id);
        Assert.DoesNotContain(toRevoke, r => r.Id == "upper");
    }
}

/// <summary>
/// A context that maps <see cref="ApiKeyRecord"/> through <see cref="ApiKeyRecordConfiguration"/> and
/// then forces the subject column to SQLite's <c>NOCASE</c> collation, reproducing a case-insensitive
/// database so the store's ordinal guard can be exercised.
/// </summary>
internal sealed class CaseInsensitiveSubjectContext : OrionLedgerDbContext
{
    public CaseInsensitiveSubjectContext(DbContextOptions<CaseInsensitiveSubjectContext> options)
        : base(options)
    {
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Override only the subject column's collation to NOCASE. The store must not rely on the column
        // collation for case sensitivity, so making the column case insensitive here is the adversarial
        // condition the ordinal guard has to survive.
        modelBuilder.Entity<ApiKeyRecord>()
            .Property(r => r.Subject)
            .UseCollation("NOCASE");
    }
}

/// <summary>
/// A disposable SQLite harness for <see cref="CaseInsensitiveSubjectContext"/>, mirroring the shared
/// store harness: a private temporary database file, schema created once, usable as the store's
/// <see cref="IDbContextFactory{TContext}"/>.
/// </summary>
internal sealed class CaseInsensitiveSubjectContextFactory
    : IDbContextFactory<CaseInsensitiveSubjectContext>, IAsyncDisposable
{
    private readonly string databasePath;
    private readonly string connectionString;

    private CaseInsensitiveSubjectContextFactory(string databasePath)
    {
        this.databasePath = databasePath;
        connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            DefaultTimeout = 30,
            Pooling = true,
        }.ToString();
    }

    public static async Task<CaseInsensitiveSubjectContextFactory> CreateAsync()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            string.Create(CultureInfo.InvariantCulture, $"orionledger-ef-ci-{Guid.NewGuid():N}.db"));

        var harness = new CaseInsensitiveSubjectContextFactory(path);

        await using var context = harness.CreateDbContext();
        await context.Database.EnsureCreatedAsync();

        return harness;
    }

    public CaseInsensitiveSubjectContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<CaseInsensitiveSubjectContext>()
            .UseSqlite(connectionString)
            .Options;
        return new CaseInsensitiveSubjectContext(options);
    }

    public async ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();

        await Task.Run(() =>
        {
            try
            {
                if (File.Exists(databasePath))
                {
                    File.Delete(databasePath);
                }
            }
            catch (IOException)
            {
                // OS reclaims the temp file; a lingering handle is not a test failure.
            }
        });
    }
}

#pragma warning restore CA1707
