# OrionLedger.Conformance

[![NuGet](https://img.shields.io/nuget/v/OrionLedger.Conformance.svg)](https://www.nuget.org/packages/OrionLedger.Conformance/)

A reusable [xUnit](https://xunit.net/) conformance suite for
[OrionLedger](https://www.nuget.org/packages/OrionLedger/) `IApiKeyStore` implementations. Write a
custom store over your database, derive one test class from `ApiKeyStoreConformanceTests`, supply a
factory that returns a fresh store, and inherit the full battery of contract tests.

Part of the **Orion** family.

## What it checks

Deriving from `ApiKeyStoreConformanceTests` runs facts that assert your store:

- stores a record and finds it again **by hash** and **by id**, and returns **null on a miss** (not
  an error);
- **round-trips scopes** exactly, including the empty set;
- persists **revocation**, the **rotation timestamps** (`SupersededAt`, `SupersededById`,
  `RetiresAt`), and the **last-used** stamp and counter through `UpdateAsync`;
- **accumulates the last-used counter** under sequential updates and, critically, **does not lose
  counts under concurrent updates** (50 parallel verifies must land a count of 50);
- returns **every record for a subject** (including revoked ones) from `FindBySubjectAsync`, matches
  the subject **case-sensitively**, and returns an **empty list** for an unknown or empty subject.

## Install

```
dotnet add package OrionLedger.Conformance
```

Add it to a test project alongside the xUnit runner packages
(`Microsoft.NET.Test.Sdk`, `xunit.runner.visualstudio`).

## Usage

```csharp
using Moongazing.OrionLedger.Conformance;
using Moongazing.OrionLedger.Storage;

public sealed class MyStoreConformanceTests : ApiKeyStoreConformanceTests
{
    // Return a fresh, empty store per test. xUnit constructs the class once per fact, so each
    // test is isolated.
    protected override Task<IApiKeyStore> CreateStoreAsync()
        => Task.FromResult<IApiKeyStore>(new MyApiKeyStore(/* fresh backing store */));

    // Optional: release per-test resources (a connection, a temporary database, a dropped schema).
    protected override Task DisposeStoreAsync(IApiKeyStore store)
        => ((MyApiKeyStore)store).DisposeAsync().AsTask();
}
```

That single class gives you the whole contract suite. Use the protected `NewRecord(...)` helper if
you want to add store-specific tests in the same class; it builds a valid `ApiKeyRecord` with a
unique id and hash on each call.

## Note on `FindBySubjectAsync`

`FindBySubjectAsync` is an optional part of the `IApiKeyStore` contract (it has a throwing default).
The subject facts in this suite expect your store to **override** it. If your store intentionally
does not support lookup by subject, do not derive the whole suite blindly; compose only the facts you
support, or override the subject tests to assert `NotSupportedException`.

## License

Licensed under the [MIT License](https://github.com/tunahanaliozturk/OrionLedger/blob/main/LICENSE).
