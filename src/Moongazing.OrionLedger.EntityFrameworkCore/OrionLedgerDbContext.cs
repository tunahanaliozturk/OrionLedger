namespace Moongazing.OrionLedger.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore;

using Moongazing.OrionLedger.Keys;

/// <summary>
/// A ready-made <see cref="DbContext"/> that maps <see cref="ApiKeyRecord"/> through
/// <see cref="ApiKeyRecordConfiguration"/>. Use it as the backing context for
/// <see cref="EfApiKeyStore{TContext}"/>, or copy the one line in <see cref="OnModelCreating"/> into
/// an existing context to host the key table alongside your own entities.
/// </summary>
public class OrionLedgerDbContext : DbContext
{
    /// <summary>Create the context with externally supplied options (provider, connection, and so on).</summary>
    /// <param name="options">The context options.</param>
    public OrionLedgerDbContext(DbContextOptions<OrionLedgerDbContext> options)
        : base(options)
    {
    }

    /// <summary>Protected constructor for a derived context that supplies its own options type.</summary>
    /// <param name="options">The context options.</param>
    protected OrionLedgerDbContext(DbContextOptions options)
        : base(options)
    {
    }

    /// <summary>The stored API key records. Plaintext tokens are never persisted, only their hash.</summary>
    public DbSet<ApiKeyRecord> ApiKeys => Set<ApiKeyRecord>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfiguration(new ApiKeyRecordConfiguration());
    }
}
