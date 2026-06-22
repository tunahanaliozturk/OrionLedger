namespace Moongazing.OrionLedger.EntityFrameworkCore.Tests;

using Moongazing.OrionLedger.Conformance;
using Moongazing.OrionLedger.Storage;

/// <summary>
/// Runs the reusable <see cref="ApiKeyStoreConformanceTests"/> contract suite against
/// <see cref="EfApiKeyStore{TContext}"/> over a real SQLite database. SQLite is used (not the EF
/// InMemory provider) so the unique hash index, transactions, and the server-side last-used
/// increment are exercised against an engine that actually enforces them.
/// </summary>
public sealed class EfApiKeyStoreConformanceTests : ApiKeyStoreConformanceTests
{
    private SqliteApiKeyStoreContext harness = null!;

    /// <inheritdoc />
    protected override async Task<IApiKeyStore> CreateStoreAsync()
    {
        harness = await SqliteApiKeyStoreContext.CreateAsync();
        return new EfApiKeyStore<OrionLedgerDbContext>(harness);
    }

    /// <inheritdoc />
    protected override async Task DisposeStoreAsync(IApiKeyStore store) =>
        await harness.DisposeAsync();
}
