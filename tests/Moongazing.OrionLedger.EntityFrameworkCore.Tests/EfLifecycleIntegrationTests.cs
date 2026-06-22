namespace Moongazing.OrionLedger.EntityFrameworkCore.Tests;

using System.Globalization;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Moongazing.OrionLedger;
using Moongazing.OrionLedger.Keys;
using Moongazing.OrionLedger.Storage;

/// <summary>
/// End-to-end tests that wire the real <see cref="ApiKeyService"/> to the EF store through the DI
/// extension and run the full public lifecycle over SQLite. This proves the registration helper,
/// the service, and the store compose, not just the store in isolation.
/// </summary>
public sealed class EfLifecycleIntegrationTests : IAsyncLifetime
{
    private readonly string databasePath = Path.Combine(
        Path.GetTempPath(),
        string.Create(CultureInfo.InvariantCulture, $"orionledger-ef-int-{Guid.NewGuid():N}.db"));

    private ServiceProvider provider = null!;

    public async Task InitializeAsync()
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            DefaultTimeout = 30,
            Pooling = true,
        }.ToString();

        var services = new ServiceCollection();

        // Register the EF store first, then OrionLedger: the in-memory store is only added when no
        // IApiKeyStore is present, so this is the documented wiring order.
        services.AddOrionLedgerEntityFrameworkCoreStore<OrionLedgerDbContext>(o => o.UseSqlite(connectionString));
        services.AddOrionLedger(o => o.Prefix = "ork_test_");

        provider = services.BuildServiceProvider();

        var factory = provider.GetRequiredService<IDbContextFactory<OrionLedgerDbContext>>();
        await using var context = await factory.CreateDbContextAsync();
        await context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await provider.DisposeAsync();
        SqliteConnection.ClearAllPools();
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
    }

    [Fact]
    public void the_ef_store_is_the_registered_store()
    {
        var store = provider.GetRequiredService<IApiKeyStore>();
        Assert.IsType<EfApiKeyStore<OrionLedgerDbContext>>(store);
    }

    [Fact]
    public async Task issue_then_verify_round_trips_and_stamps_last_used()
    {
        var keys = provider.GetRequiredService<IApiKeyService>();

        var issued = await keys.IssueAsync("Acme", scopes: ["orders:read"]);
        var result = await keys.VerifyAsync(issued.Token, requiredScope: "orders:read");

        Assert.True(result.IsValid);
        Assert.Equal(ApiKeyStatus.Valid, result.Status);
        Assert.NotNull(result.Record);
        Assert.Equal(1, result.Record.LastUsedCount);
        Assert.NotNull(result.Record.LastUsedAt);
    }

    [Fact]
    public async Task a_wrong_scope_verifies_as_missing_scope()
    {
        var keys = provider.GetRequiredService<IApiKeyService>();

        var issued = await keys.IssueAsync("Acme", scopes: ["orders:read"]);
        var result = await keys.VerifyAsync(issued.Token, requiredScope: "orders:write");

        Assert.Equal(ApiKeyStatus.MissingScope, result.Status);
    }

    [Fact]
    public async Task revoke_makes_the_key_verify_as_revoked()
    {
        var keys = provider.GetRequiredService<IApiKeyService>();

        var issued = await keys.IssueAsync("Acme");
        Assert.True(await keys.RevokeAsync(issued.Record.Id));

        var result = await keys.VerifyAsync(issued.Token);
        Assert.Equal(ApiKeyStatus.Revoked, result.Status);

        // Revoking again is idempotent against the persisted state.
        Assert.False(await keys.RevokeAsync(issued.Record.Id));
    }

    [Fact]
    public async Task rotation_supersedes_the_predecessor_and_issues_a_working_successor()
    {
        var keys = provider.GetRequiredService<IApiKeyService>();

        var issued = await keys.IssueAsync("Acme", scopes: ["a"]);
        var rotation = await keys.RotateAsync(issued.Record.Id);

        Assert.NotNull(rotation);

        // No grace: the old token is retired immediately (revoked), the new token verifies.
        var oldResult = await keys.VerifyAsync(issued.Token);
        Assert.Equal(ApiKeyStatus.Revoked, oldResult.Status);

        var newResult = await keys.VerifyAsync(rotation.Token, requiredScope: "a");
        Assert.True(newResult.IsValid);
    }

    [Fact]
    public async Task bulk_revoke_by_subject_revokes_every_active_key()
    {
        var keys = provider.GetRequiredService<IApiKeyService>();

        var first = await keys.IssueAsync("Acme web", subject: "tenant-42");
        var second = await keys.IssueAsync("Acme worker", subject: "tenant-42");
        await keys.IssueAsync("Other", subject: "tenant-other");

        var revoked = await keys.RevokeAllForSubjectAsync("tenant-42");
        Assert.Equal(2, revoked);

        Assert.Equal(ApiKeyStatus.Revoked, (await keys.VerifyAsync(first.Token)).Status);
        Assert.Equal(ApiKeyStatus.Revoked, (await keys.VerifyAsync(second.Token)).Status);
    }

    [Fact]
    public async Task many_verifies_accumulate_an_exact_count_through_the_service()
    {
        var keys = provider.GetRequiredService<IApiKeyService>();
        var issued = await keys.IssueAsync("Acme");

        // Sequential verifies through the full service path must produce an exact count, because the
        // store applies each increment at the database.
        for (var i = 0; i < 10; i++)
        {
            var result = await keys.VerifyAsync(issued.Token);
            Assert.True(result.IsValid);
        }

        var store = provider.GetRequiredService<IApiKeyStore>();
        var record = await store.FindByIdAsync(issued.Record.Id);
        Assert.NotNull(record);
        Assert.Equal(10, record.LastUsedCount);
    }
}
