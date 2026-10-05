# OrionLedger Features

A deep breakdown of what the package does and the public surface behind each capability. Every type
named here is public unless noted. The core package is `OrionLedger`; the root namespace is
`Moongazing.OrionLedger`. The companion packages (`OrionLedger.AspNetCore`,
`OrionLedger.EntityFrameworkCore`, `OrionLedger.Conformance`) are summarised in
[section 14](#14-companion-packages).

> **Scope of the name.** OrionLedger is an API key lifecycle library, not a financial ledger. The
> "ledger" it keeps is the set of issued keys and their state (scopes, expiry, revocation, last
> use). No monetary concepts exist in the package.

---

## Table of contents

1. [Key issuance](#1-key-issuance)
2. [Verification](#2-verification)
3. [Scopes](#3-scopes)
4. [Expiry](#4-expiry)
5. [Revocation](#5-revocation)
6. [Rotation](#6-rotation)
7. [Bulk revoke by subject](#7-bulk-revoke-by-subject)
8. [Last-used tracking](#8-last-used-tracking)
9. [Storage](#9-storage)
10. [Token generation and hashing](#10-token-generation-and-hashing)
11. [Telemetry](#11-telemetry)
12. [Lifecycle observer](#12-lifecycle-observer)
13. [Configuration and registration](#13-configuration-and-registration)
14. [Companion packages](#14-companion-packages)
15. [Targeting and build](#15-targeting-and-build)

---

## 1. Key issuance

`IApiKeyService.IssueAsync` is the entry point:

```csharp
Task<IssuedApiKey> IssueAsync(
    string name,
    IEnumerable<string>? scopes = null,
    DateTimeOffset? expiresAt = null,
    string? subject = null,
    CancellationToken cancellationToken = default);
```

Issuance generates a fresh plaintext token, hashes it for storage, builds an `ApiKeyRecord`, and
persists the record. The plaintext is returned on `IssuedApiKey.Token` and never stored.

`IssuedApiKey` exposes exactly two members:

- `Token` - the plaintext key, available only here. It cannot be recovered later.
- `Record` - the persisted `ApiKeyRecord` (no plaintext).

`name` is required (an empty name throws). It is a human-recognisable label, typically a tenant or
application name, surfaced later for logging via `Record.Name`. `subject` is the optional owner
(a user, tenant, or service id) that groups keys for bulk revocation.

`ApiKeyRecord` is the stored shape:

| Member | Type | Mutability | Meaning |
|--------|------|------------|---------|
| `Id` | `string` | init | Stable identifier assigned at issuance (the revoke handle) |
| `Name` | `string` | init | The label supplied at issuance |
| `Subject` | `string?` | init | The owner the key belongs to, or null |
| `DisplayPrefix` | `string` | init | Non-secret leading characters of the token, for recognition |
| `Hash` | `string` | init | SHA-256 hash of the token; the verification lookup key |
| `Scopes` | `IReadOnlySet<string>` | init | Granted scopes (ordinal comparer) |
| `CreatedAt` | `DateTimeOffset` | init | Issue time |
| `ExpiresAt` | `DateTimeOffset?` | init | Expiry, or null for never |
| `RevokedAt` | `DateTimeOffset?` | set | Revoke time, or null while active |
| `LastUsedAt` | `DateTimeOffset?` | set | Last successful verification, or null |
| `LastUsedCount` | `long` | set | Successful verifications (best-effort in memory, exact in the EF Core store) |
| `SupersededAt` | `DateTimeOffset?` | set | When the key was rotated, or null |
| `SupersededById` | `string?` | set | Id of the successor key, or null |
| `RetiresAt` | `DateTimeOffset?` | set | End of the rotation grace window, or null |

Only the `set` members mutate over a key's life; everything else is fixed at issuance.

---

## 2. Verification

`VerifyAsync` resolves a presented token to a single status:

```csharp
Task<ApiKeyVerification> VerifyAsync(
    string? token,
    string? requiredScope = null,
    CancellationToken cancellationToken = default);
```

The checks run in a fixed order and short-circuit at the first failure:

1. **Prefix / emptiness.** If the token is null, empty, or does not start with the configured prefix
   (ordinal compare), the result is `Malformed`. No store lookup happens.
2. **Hash lookup.** The token is hashed and looked up by hash. No match is `NotFound`.
3. **Revocation.** A record with a non-null `RevokedAt` is `Revoked`.
4. **Retirement.** A rotated record whose `RetiresAt` is at or before now is `Retired`. This runs
   before expiry, so a key that is both retired and expired reports the rotation outcome.
5. **Expiry.** A record whose `ExpiresAt` is at or before now is `Expired`.
6. **Scope.** If `requiredScope` is supplied and the record's scopes do not contain it, the result is
   `MissingScope`.
7. **Valid.** Otherwise the result is `Valid`: the record's `LastUsedAt` is stamped,
   `LastUsedCount` is incremented, and both are persisted through `UpdateAsync`.

`ApiKeyVerification` exposes:

- `Status` - the `ApiKeyStatus` enum value.
- `IsValid` - true only when `Status == Valid`.
- `Record` - the matched record, present for `Valid`, `Expired`, `Revoked`, `Retired`, and
  `MissingScope`; null for `Malformed` and `NotFound`.

`ApiKeyStatus`: `Valid`, `Malformed`, `NotFound`, `Expired`, `Revoked`, `Retired`, `MissingScope`.

Returning the record on the rejected-but-known statuses lets you log which key was refused without
re-querying.

---

## 3. Scopes

Scopes are arbitrary strings granted at issuance and checked one at a time at verification. Matching
is exact and ordinal (case-sensitive); scopes are held in a `HashSet<string>` with
`StringComparer.Ordinal`. There is no wildcard or hierarchy in the package; a scope either is or is
not present.

- `IssueAsync(name, scopes: ["orders:read", "orders:write"])` grants two scopes.
- `VerifyAsync(token, requiredScope: "orders:write")` requires one.
- `VerifyAsync(token)` (or `requiredScope: null`) skips the scope check while still validating
  prefix, existence, revocation, and expiry.

---

## 4. Expiry

Expiry is resolved at issuance into the record's `ExpiresAt`:

1. An explicit `expiresAt` argument to `IssueAsync` wins.
2. Otherwise, if `ApiKeyOptions.DefaultLifetime` is set, expiry is the issue time plus that span.
3. Otherwise `ExpiresAt` is null and the key never expires.

At verification a key is `Expired` when `ExpiresAt <= now`. The clock is injectable internally for
deterministic tests.

---

## 5. Revocation

```csharp
Task<bool> RevokeAsync(string id, CancellationToken cancellationToken = default);
```

Revocation is by record id. It returns:

- `true` when the key was found and not yet revoked; its `RevokedAt` is stamped and persisted.
- `false` when no key has that id, or the key was already revoked.

The `false`-on-already-revoked behaviour makes repeated calls idempotent. A revoked key thereafter
verifies as `Revoked`. Revocation is permanent; there is no un-revoke.

---

## 6. Rotation

```csharp
Task<KeyRotation?> RotateAsync(string id, TimeSpan? grace = null, CancellationToken cancellationToken = default);
```

Rotation issues a successor key (its own id, secret, and hash) that inherits the predecessor's name,
subject, scopes, and absolute `ExpiresAt`; `DefaultLifetime` is not applied again. The predecessor
gets `SupersededAt` and `SupersededById`.

- A positive `grace` sets `RetiresAt = now + grace`: the old token keeps verifying as `Valid` until
  then and as `Retired` afterwards.
- A null or zero `grace` revokes the predecessor immediately. A negative `grace` throws
  `ArgumentOutOfRangeException`.
- A missing, revoked, expired, or already superseded key is not rotated; the call returns null.

`KeyRotation` exposes `Successor` (an `IssuedApiKey`), `Predecessor` (the superseded record), and
`Token` (shortcut to `Successor.Token`, the only plaintext copy of the new key).

---

## 7. Bulk revoke by subject

```csharp
Task<int> RevokeAllForSubjectAsync(string subject, CancellationToken cancellationToken = default);
```

Revokes every active key whose `Subject` matches (ordinal, case-sensitive) and returns how many it
newly revoked. Keys that are already revoked, expired, or past `RetiresAt` are left untouched. It
needs `IApiKeyStore.FindBySubjectAsync`; a store that keeps the default implementation throws
`NotSupportedException`.

---

## 8. Last-used tracking

A `Valid` verification stamps the record's `LastUsedAt` with the current time, increments
`LastUsedCount`, and persists both through the store's `UpdateAsync`. Only successful verifications update it; rejected attempts do not. This
gives you a cheap "when was this key last actually used" signal for key hygiene and stale-key
cleanup.

---

## 9. Storage

`IApiKeyStore` is the persistence seam: four required methods and one optional one.

```csharp
Task AddAsync(ApiKeyRecord record, CancellationToken ct = default);
Task<ApiKeyRecord?> FindByHashAsync(string hash, CancellationToken ct = default);
Task<ApiKeyRecord?> FindByIdAsync(string id, CancellationToken ct = default);
Task UpdateAsync(ApiKeyRecord record, CancellationToken ct = default);
Task<IReadOnlyList<ApiKeyRecord>> FindBySubjectAsync(string subject, CancellationToken ct = default); // default throws
```

- `FindByHashAsync` is the verification hot path; the hash column should be indexed.
- `FindByIdAsync` backs revocation, rotation, and administration.
- `UpdateAsync` persists the mutable fields: `RevokedAt`, `LastUsedAt`, `LastUsedCount`,
  `SupersededAt`, `SupersededById`, and `RetiresAt`.
- `FindBySubjectAsync` is a default interface method that throws `NotSupportedException`; override
  it to enable bulk revoke by subject.

`InMemoryApiKeyStore` is the bundled implementation: two `ConcurrentDictionary` indexes (by hash and
by id) over the same record instances. It is process-local, so it does not survive a restart and is
not shared across instances. It suits a single instance, tests, and getting started; use a
database-backed store for anything multi-instance or durable.

To use a custom store, register your `IApiKeyStore` before `AddOrionLedger()`; the in-memory store is
only registered if no `IApiKeyStore` is already present (`TryAddSingleton`).

---

## 10. Token generation and hashing

`ApiKeyGenerator` (static) produces tokens of the form `<prefix><base64url-secret>`:

- `Generate(string prefix, int secretByteLength)` draws `secretByteLength` cryptographically random
  bytes via `RandomNumberGenerator` and base64url-encodes them onto the prefix. `secretByteLength`
  must be at least 16.
- `DisplayPrefix(string token)` returns the leading `DisplayPrefixLength` (12) characters, the
  non-secret portion stored as `ApiKeyRecord.DisplayPrefix` and safe to show to admins.

`ApiKeyHasher` (static) handles storage and comparison:

- `Hash(string token)` returns a 64-character lowercase hex SHA-256 digest. This is what storage
  holds and what verification looks up by.
- `FixedTimeEquals(string a, string b)` wraps `CryptographicOperations.FixedTimeEquals` for a
  constant-time hash comparison, so a direct comparison does not leak match length through timing.

A fast cryptographic hash is the correct choice here precisely because keys are full-entropy random
tokens: there is no weak secret to slow down an attacker against, and verification stays O(1). This
reasoning does not transfer to user passwords, which need a deliberately slow hash.

---

## 11. Telemetry

`ApiKeyDiagnostics` derives from `OrionInstrumentation` (from `Orion.Abstractions`) and owns an
OpenTelemetry `Meter` named `Moongazing.OrionLedger` (`ApiKeyDiagnostics.MeterName`), registered as a
singleton and disposable. Four counters:

| Instrument | Unit | Tags | Increments on |
|------------|------|------|---------------|
| `orion.ledger.keys.issued` | `{key}` | - | Each issued key |
| `orion.ledger.verifications` | `{verification}` | `status` | Each verification attempt |
| `orion.ledger.keys.revoked` | `{key}` | - | Each revoked key, including keys swept by bulk revoke |
| `orion.ledger.keys.rotated` | `{key}` | - | Each rotation |

The `status` tag on `orion.ledger.verifications` takes one of `valid`, `malformed`, `not_found`,
`expired`, `revoked`, `retired`, `missing_scope`. Static tags set through
`OrionInstrumentation.SetStaticTags` are added to every measurement. Subscribe with the standard metrics pipeline:

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(m => m.AddMeter(ApiKeyDiagnostics.MeterName));
```

---

## 12. Lifecycle observer

`IApiKeyEventObserver` is an optional audit hook with four callbacks:

```csharp
void OnIssued(ApiKeyRecord record);
void OnVerified(ApiKeyVerification verification);
void OnRevoked(ApiKeyRecord record);                                   // once per key in a bulk revoke
void OnRotated(ApiKeyRecord predecessor, ApiKeyRecord successor) { }   // default no-op
```

Register one and it is invoked after each corresponding operation. The service treats observers as
observability, not load-bearing logic: any exception an observer throws is caught and swallowed so it
can never block issuance, verification, revocation, or rotation. When no observer is registered, the
public no-op `NullApiKeyEventObserver.Instance` is used.

`OnVerified` fires for every attempt, including rejected ones, so it is the natural place to build a
full audit trail of who presented what and what the outcome was.

---

## 13. Configuration and registration

`AddOrionLedger(this IServiceCollection, Action<ApiKeyOptions>? configure = null)` registers:

- The configured `ApiKeyOptions` (validated immediately; invalid options throw at registration).
- `ApiKeyDiagnostics` as a singleton.
- `InMemoryApiKeyStore` as `IApiKeyStore`, only if none is already registered.
- `IApiKeyService` as a singleton `ApiKeyService`, wired to whatever `IApiKeyEventObserver` is
  present (or none).

All registrations use `TryAdd*`, so you can override any of them by registering your own
implementation first.

`ApiKeyOptions`:

| Option | Type | Default | Constraint |
|--------|------|---------|------------|
| `Prefix` | `string` | `ork_` | Non-empty |
| `SecretByteLength` | `int` | `32` | At least 16 |
| `DefaultLifetime` | `TimeSpan?` | `null` | Positive when set |

`Validate()` enforces these constraints and runs both at registration and on service construction.

---

## 14. Companion packages

- **`OrionLedger.AspNetCore`** - `AddAuthentication().AddOrionLedgerApiKey(...)` registers
  `ApiKeyAuthenticationHandler`, which reads the key from `ApiKeyAuthenticationOptions.HeaderName`
  (default `X-Api-Key`), calls `VerifyAsync` without a scope, and on `Valid` builds a principal with
  the key id, subject, and one claim per scope. `RequireApiKeyScope` and `AddApiKeyScopePolicy` turn
  scopes into authorization policies (require any or require all), bound to the API key scheme.
- **`OrionLedger.EntityFrameworkCore`** - `EfApiKeyStore<TContext>` implements every
  `IApiKeyStore` method over EF Core, mapped by `ApiKeyRecordConfiguration` (table
  `OrionLedgerApiKeys`, unique hash index, subject index, JSON scopes) and registered with
  `AddOrionLedgerEntityFrameworkCoreStore<TContext>()`. `LastUsedCount` is applied as an atomic
  server-side increment, and each update writes only the lifecycle columns that changed.
- **`OrionLedger.Conformance`** - `ApiKeyStoreConformanceTests`, an abstract xUnit class: implement
  `CreateStoreAsync` and inherit the store contract tests.

---

## 15. Targeting and build

- Multi-targets `net8.0`, `net9.0`, `net10.0`.
- Nullable reference types enabled; implicit usings enabled.
- `TreatWarningsAsErrors` with latest recommended analyzers and enforced code style.
- XML documentation is generated and shipped with the package.
- The runtime dependencies are `Microsoft.Extensions.DependencyInjection.Abstractions` and `Orion.Abstractions` (the family's shared contracts spine).
