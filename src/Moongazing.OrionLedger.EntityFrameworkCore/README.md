# OrionLedger.EntityFrameworkCore

[![NuGet](https://img.shields.io/nuget/v/OrionLedger.EntityFrameworkCore.svg)](https://www.nuget.org/packages/OrionLedger.EntityFrameworkCore/)

An Entity Framework Core reference store for [OrionLedger](https://www.nuget.org/packages/OrionLedger/).
It implements `IApiKeyStore` over EF Core so issued keys survive a restart and are shared across
instances, instead of living in the process-local in-memory store.

Part of the **Orion** family.

## What it does

- **Maps `ApiKeyRecord`** to a relational table through `ApiKeyRecordConfiguration`, with the token
  **hash uniquely indexed** (it is the verification lookup key, so lookups are an index seek) and the
  subject indexed (it backs bulk revoke).
- **Persists every mutable lifecycle field**: `RevokedAt`, `LastUsedAt`, `LastUsedCount`, and the
  rotation timestamps (`SupersededAt`, `SupersededById`, `RetiresAt`).
- **Increments last-used atomically.** A successful verify applies `LastUsedCount = LastUsedCount + 1`
  as a server-side update, so two concurrent verifications both land their increment rather than
  racing on a read-modify-write and losing a count.
- **Ships `FindBySubjectAsync`**, so `RevokeAllForSubjectAsync` (bulk revoke by owner) works out of
  the box.
- Provider agnostic: depends only on `Microsoft.EntityFrameworkCore.Relational`, so you choose the
  database provider (SQL Server, PostgreSQL, SQLite, and so on).

## Install

```
dotnet add package OrionLedger.EntityFrameworkCore
```

You also need an EF Core provider package for your database, for example
`Microsoft.EntityFrameworkCore.SqlServer` or `Npgsql.EntityFrameworkCore.PostgreSQL`.

## Quick start

Register the store **before** `AddOrionLedger()`, configuring the context inline. The bundled
`OrionLedgerDbContext` is ready to use:

```csharp
using Moongazing.OrionLedger.EntityFrameworkCore;

// Register the EF store first: AddOrionLedger only adds the in-memory store if no IApiKeyStore
// is already present. This also registers a pooled context factory the store resolves a
// short-lived context from per operation.
builder.Services.AddOrionLedgerEntityFrameworkCoreStore<OrionLedgerDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("Keys")));

builder.Services.AddOrionLedger(o => o.Prefix = "ork_live_");
```

That is all the wiring the lifecycle needs: `IssueAsync`, `VerifyAsync`, `RotateAsync`,
`RevokeAsync`, and `RevokeAllForSubjectAsync` now persist through EF Core.

The store opens its own context per operation, so a single registration is safe under concurrent
verifications. If you already register an `IDbContextFactory<TContext>` yourself, call the
parameterless `AddOrionLedgerEntityFrameworkCoreStore<TContext>()` overload instead.

## Using your own DbContext

If you already have a context, host the key table in it by applying the configuration in
`OnModelCreating`, then point the store at that context:

```csharp
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfiguration(new ApiKeyRecordConfiguration());
        // ... your own entities
    }
}

builder.Services.AddOrionLedgerEntityFrameworkCoreStore<AppDbContext>();
```

`ApiKeyRecordConfiguration` also accepts a custom table name if the default `OrionLedgerApiKeys`
clashes with an existing table.

## Migrations

The store does not create the schema. Add a migration for the mapped entity the usual way and apply
it as part of your deployment:

```
dotnet ef migrations add AddOrionLedgerApiKeys
dotnet ef database update
```

## Concurrency note

`LastUsedCount` is exact under this store, not best-effort: the increment is applied in the database,
so concurrent verifications do not lose counts. The remaining lifecycle fields (revocation and
rotation timestamps) are last-writer-wins, which matches their intent: revoking an already-revoked
key or re-stamping a rotation is idempotent.

## Versioning

Multi-targets `net8.0`, `net9.0`, and `net10.0`, pinning the matching EF Core major per target
framework. Tracks the OrionLedger version line. See the
[root README](https://github.com/tunahanaliozturk/OrionLedger) and
[CHANGELOG](https://github.com/tunahanaliozturk/OrionLedger/blob/main/CHANGELOG.md).

## License

Licensed under the [MIT License](https://github.com/tunahanaliozturk/OrionLedger/blob/main/LICENSE).
