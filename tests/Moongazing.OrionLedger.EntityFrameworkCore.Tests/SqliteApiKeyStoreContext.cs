namespace Moongazing.OrionLedger.EntityFrameworkCore.Tests;

using System.Globalization;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// A disposable SQLite-backed harness for the EF store. It owns a private temporary database file,
/// creates the schema once, and acts as the <see cref="IDbContextFactory{TContext}"/> the store
/// resolves contexts from, so each operation opens its own connection and the store's real
/// cross-connection concurrency is exercised the way it would be in production.
/// </summary>
/// <remarks>
/// A file-based database is used rather than a single shared in-memory connection so that concurrent
/// updates open distinct connections and contend through SQLite's own locking. A busy timeout lets a
/// brief writer collision retry instead of surfacing "database is locked", which is the realistic
/// behaviour of a server provider under contention.
/// </remarks>
internal sealed class SqliteApiKeyStoreContext : IDbContextFactory<OrionLedgerDbContext>, IAsyncDisposable
{
    private readonly string databasePath;
    private readonly string connectionString;

    private SqliteApiKeyStoreContext(string databasePath)
    {
        this.databasePath = databasePath;
        connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            // Serialize writers in-process with a generous wait so the concurrent-increment test
            // contends realistically rather than throwing on the first lock.
            DefaultTimeout = 30,
            Pooling = true,
        }.ToString();
    }

    /// <summary>Create the harness and the schema.</summary>
    /// <returns>A ready harness whose database has the key table.</returns>
    public static async Task<SqliteApiKeyStoreContext> CreateAsync()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            string.Create(CultureInfo.InvariantCulture, $"orionledger-ef-{Guid.NewGuid():N}.db"));

        var harness = new SqliteApiKeyStoreContext(path);

        await using var context = harness.CreateDbContext();
        await context.Database.EnsureCreatedAsync();

        return harness;
    }

    /// <summary>Open a fresh context on the harness database.</summary>
    /// <returns>A new <see cref="OrionLedgerDbContext"/>.</returns>
    public OrionLedgerDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<OrionLedgerDbContext>()
            .UseSqlite(connectionString)
            .Options;
        return new OrionLedgerDbContext(options);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        // Clear the connection pool so the file handle is released before deletion on Windows.
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
                // A lingering handle on a loaded CI runner is not a test failure; the temp file is
                // reclaimed by the OS. Swallow rather than fail teardown.
            }
        });
    }
}
