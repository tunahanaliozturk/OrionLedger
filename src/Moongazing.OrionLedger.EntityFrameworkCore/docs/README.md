# OrionLedger.EntityFrameworkCore

An Entity Framework Core `IApiKeyStore` for OrionLedger, so issued API keys survive a restart and are shared across instances instead of living in the process-local in-memory store.

![OrionLedger overview: OrionLedger.EntityFrameworkCore implements IApiKeyStore for the core and persists records to a relational database](https://raw.githubusercontent.com/tunahanaliozturk/OrionLedger/main/docs/diagrams/overview.png)

## Install

    dotnet add package OrionLedger.EntityFrameworkCore

It plugs into `OrionLedger` and depends only on `Microsoft.EntityFrameworkCore.Relational`. Add the EF Core provider for your database as well, for example `Microsoft.EntityFrameworkCore.SqlServer` or `Npgsql.EntityFrameworkCore.PostgreSQL`.

## Quick start

Register the store before `AddOrionLedger()`; the in-memory store is only added when no `IApiKeyStore` is registered yet.

```csharp
using Microsoft.EntityFrameworkCore;
using Moongazing.OrionLedger;
using Moongazing.OrionLedger.EntityFrameworkCore;

// Registers a pooled IDbContextFactory<OrionLedgerDbContext> and EfApiKeyStore<OrionLedgerDbContext>.
builder.Services.AddOrionLedgerEntityFrameworkCoreStore<OrionLedgerDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("Keys")));

builder.Services.AddOrionLedger(o => o.Prefix = "ork_live_");
```

`IssueAsync`, `VerifyAsync`, `RotateAsync`, `RevokeAsync` and `RevokeAllForSubjectAsync` now persist through EF Core. The store creates a short-lived context per operation from the factory, so one singleton registration is safe under concurrent requests.

## Using your own DbContext

Apply the mapping in your context and point the store at it. If you register the `IDbContextFactory<TContext>` yourself, use the parameterless overload:

```csharp
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfiguration(new ApiKeyRecordConfiguration());
    }
}

builder.Services.AddDbContextFactory<AppDbContext>(o => o.UseNpgsql(connectionString));
builder.Services.AddOrionLedgerEntityFrameworkCoreStore<AppDbContext>();
```

`ApiKeyRecordConfiguration` maps to the table `OrionLedgerApiKeys` by default. Its constructors take a table name, and optionally a provider-specific case-sensitive collation for the `Subject` column.

## Mapping

- `Id` is the primary key (never database generated). `Hash` is required and uniquely indexed: it is the verification lookup key. `Subject` is indexed for bulk revoke.
- `Scopes` is stored as a JSON array column with a value comparer, so the exact ordinal set round-trips.
- The store does not create the schema. Add a migration (`dotnet ef migrations add AddOrionLedgerApiKeys`) and apply it in your deployment.

## Concurrency

- `LastUsedCount` is exact: `UpdateAsync` applies it as a server-side `LastUsedCount + delta` in one `ExecuteUpdate`, so concurrent verifications do not lose counts.
- Each update writes only the lifecycle columns the operation changed (`RevokedAt`, `LastUsedAt`, `SupersededAt`, `SupersededById`, `RetiresAt`); the others keep their current database value. A verify's last-used update therefore never undoes a revoke or rotation that landed after the record was read.
- `FindBySubjectAsync` filters ordinally in memory after the indexed query, so bulk revoke is case-sensitive even on a case-insensitive database collation.
- `UpdateAsync` for an id with no row throws `InvalidOperationException`.

Not AOT- or trim-compatible: EF Core itself is not. Targets net8.0, net9.0 and net10.0 with the matching EF Core major per target.

## Related packages

- `OrionLedger` - the key lifecycle core this store plugs into.
- `OrionLedger.Conformance` - the contract suite this store passes; run it against your own store too.
- `OrionLedger.AspNetCore` - API key authentication for ASP.NET Core.

## Links

- Documentation and full README: https://github.com/tunahanaliozturk/OrionLedger
- Changelog: https://github.com/tunahanaliozturk/OrionLedger/blob/main/CHANGELOG.md
- License: MIT
