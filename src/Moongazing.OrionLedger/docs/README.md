# OrionLedger

API key lifecycle for .NET: issue prefixed, high-entropy keys, store only their SHA-256 hash, verify a presented key to a single status, rotate and revoke keys, and track when each key was last used.

![How OrionLedger issues a key and verifies it: generate, hash and store the record, return the token once; verification checks prefix, hash lookup, revocation, retirement, expiry and scope in order](https://raw.githubusercontent.com/tunahanaliozturk/OrionLedger/main/docs/diagrams/issue-verify.png)

## Install

    dotnet add package OrionLedger

## Quick start

```csharp
using Moongazing.OrionLedger;
using Moongazing.OrionLedger.Keys;

builder.Services.AddOrionLedger(o =>
{
    o.Prefix = "ork_live_";
    o.DefaultLifetime = TimeSpan.FromDays(90);   // optional
});

// Issue: the plaintext token exists only here. Show it once, keep only issued.Record.
IssuedApiKey issued = await keys.IssueAsync("Acme Corp", scopes: ["orders:read", "orders:write"]);
string token = issued.Token;

// Verify a presented token, optionally requiring a scope.
ApiKeyVerification result = await keys.VerifyAsync(token, requiredScope: "orders:write");
if (!result.IsValid)
{
    // Malformed, NotFound, Revoked, Retired, Expired or MissingScope
}
```

`keys` is the `IApiKeyService` that `AddOrionLedger` registers.

## Lifecycle

- `IssueAsync(name, scopes, expiresAt, subject)` returns an `IssuedApiKey`; `Token` is the only plaintext copy.
- `VerifyAsync(token, requiredScope)` returns an `ApiKeyVerification` with `Status`, `IsValid` and the matched `Record` (null only for `Malformed` and `NotFound`). A `Valid` result stamps `LastUsedAt` and increments `LastUsedCount`.
- `RotateAsync(id, grace)` issues a successor with the same name, subject, scopes and expiry. With a positive grace the old token keeps verifying until `RetiresAt`, then resolves as `Retired`; with no grace it is revoked at once. Returns null for a missing, revoked, expired or already rotated key.
- `RevokeAsync(id)` returns `false` when the key is missing or already revoked. `RevokeAllForSubjectAsync(subject)` revokes every active key of a subject and returns the count.

## Options

`ApiKeyOptions`, validated inside `AddOrionLedger` (an invalid value throws there):

- `Prefix` - prepended to every token; must not be empty. Default `ork_`.
- `SecretByteLength` - random bytes in the secret; at least 16. Default 32 (256 bits).
- `DefaultLifetime` - expiry for keys issued without `expiresAt`; positive when set. Default null (no expiry).

## Storage

The default `InMemoryApiKeyStore` is process-local. Register your own `IApiKeyStore` before `AddOrionLedger()` to replace it, or install `OrionLedger.EntityFrameworkCore`. Bulk revoke needs the store to override `IApiKeyStore.FindBySubjectAsync`; the default throws `NotSupportedException`.

## Telemetry and audit

- Meter `Moongazing.OrionLedger` (`ApiKeyDiagnostics.MeterName`): counters `orion.ledger.keys.issued`, `orion.ledger.verifications` (tag `status`), `orion.ledger.keys.revoked` and `orion.ledger.keys.rotated`. Built on `OrionInstrumentation` from `Orion.Abstractions`.
- `IApiKeyEventObserver` gets `OnIssued`, `OnVerified`, `OnRevoked` and `OnRotated`. An exception it throws is swallowed and never blocks the operation.
- Targets net8.0, net9.0 and net10.0. A NativeAOT publish of the lifecycle is smoke-tested in CI.

## Related packages

- `OrionLedger.AspNetCore` - authentication handler that reads the key from a header and maps scopes to authorization policies.
- `OrionLedger.EntityFrameworkCore` - durable `IApiKeyStore` over EF Core with an atomic last-used increment.
- `OrionLedger.Conformance` - xUnit contract suite for a custom `IApiKeyStore`.

## Links

- Documentation and full README: https://github.com/tunahanaliozturk/OrionLedger
- Changelog: https://github.com/tunahanaliozturk/OrionLedger/blob/main/CHANGELOG.md
- License: MIT
