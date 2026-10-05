# OrionLedger.Conformance

A reusable xUnit contract suite for OrionLedger `IApiKeyStore` implementations: derive one test class, return a fresh store, and inherit every contract test.

## Install

    dotnet add package OrionLedger.Conformance

Add it to an xUnit test project (with `Microsoft.NET.Test.Sdk` and `xunit.runner.visualstudio`). It references `OrionLedger` and `xunit` 2.x.

## Quick start

```csharp
using Moongazing.OrionLedger.Conformance;
using Moongazing.OrionLedger.Storage;

public sealed class MyStoreConformanceTests : ApiKeyStoreConformanceTests
{
    // Called once per fact: return a fresh, empty store.
    protected override Task<IApiKeyStore> CreateStoreAsync()
        => Task.FromResult<IApiKeyStore>(new MyApiKeyStore(/* fresh backing store */));

    // Optional: release per-test resources (a connection, a temporary database).
    protected override Task DisposeStoreAsync(IApiKeyStore store)
        => ((MyApiKeyStore)store).DisposeAsync().AsTask();
}
```

Inside the class, `Store` is the store under test, `NewRecord(subject, scopes)` builds a valid `ApiKeyRecord` with a unique id and hash, and `BaseTime` is a fixed timestamp, for store-specific facts of your own.

## What it checks

- A record is found again by hash and by id; a miss returns null, not an exception.
- Scopes round-trip exactly, including the empty set.
- `UpdateAsync` persists revocation, the rotation fields (`SupersededAt`, `SupersededById`, `RetiresAt`) and the last-used stamp and count.
- Sequential last-used updates accumulate, and 50 concurrent updates land a count of exactly 50 (no lost increments).
- `FindBySubjectAsync` returns every record of a subject (revoked ones included), matches case-sensitively, and returns an empty list for an unknown or empty subject.

## FindBySubjectAsync

`FindBySubjectAsync` has a default implementation that throws `NotSupportedException`. The subject facts expect your store to override it. The facts are not virtual, so they cannot be switched off one by one: a store that keeps the default fails those four facts.

## Related packages

- `OrionLedger` - defines `IApiKeyStore`, `ApiKeyRecord` and the in-memory store.
- `OrionLedger.EntityFrameworkCore` - the reference EF Core store, which runs this suite against SQLite in CI.

## Links

- Documentation and full README: https://github.com/tunahanaliozturk/OrionLedger
- Changelog: https://github.com/tunahanaliozturk/OrionLedger/blob/main/CHANGELOG.md
- License: MIT
