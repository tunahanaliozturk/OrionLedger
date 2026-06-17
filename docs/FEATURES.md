# OrionLedger Features

A deep breakdown of what the package does and the public surface behind each capability. Every type
named here is public unless noted. The package is `OrionLedger`; the root namespace is
`Moongazing.OrionLedger`.

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
6. [Last-used tracking](#6-last-used-tracking)
7. [Storage](#7-storage)
8. [Token generation and hashing](#8-token-generation-and-hashing)
9. [Telemetry](#9-telemetry)
10. [Lifecycle observer](#10-lifecycle-observer)
11. [Configuration and registration](#11-configuration-and-registration)
12. [Targeting and build](#12-targeting-and-build)

---

## 1. Key issuance

`IApiKeyService.IssueAsync` is the entry point:

```csharp
Task<IssuedApiKey> IssueAsync(
    string name,
    IEnumerable<string>? scopes = null,
    DateTimeOffset? expiresAt = null,
    CancellationToken cancellationToken = default);
```

Issuance generates a fresh plaintext token, hashes it for storage, builds an `ApiKeyRecord`, and
persists the record. The plaintext is returned on `IssuedApiKey.Token` and never stored.

`IssuedApiKey` exposes exactly two members:

- `Token` - the plaintext key, available only here. It cannot be recovered later.
- `Record` - the persisted `ApiKeyRecord` (no plaintext).

`name` is required (an empty name throws). It is a human-recognisable label, typically a tenant or
application name, surfaced later for logging via `Record.Name`.

`ApiKeyRecord` is the stored shape:

| Member | Type | Mutability | Meaning |
|--------|------|------------|---------|
| `Id` | `string` | init | Stable identifier assigned at issuance (the revoke handle) |
| `Name` | `string` | init | The label supplied at issuance |
| `DisplayPrefix` | `string` | init | Non-secret leading characters of the token, for recognition |
| `Hash` | `string` | init | SHA-256 hash of the token; the verification lookup key |
| `Scopes` | `IReadOnlySet<string>` | init | Granted scopes (ordinal comparer) |
| `CreatedAt` | `DateTimeOffset` | init | Issue time |
| `ExpiresAt` | `DateTimeOffset?` | init | Expiry, or null for never |
| `RevokedAt` | `DateTimeOffset?` | set | Revoke time, or null while active |
| `LastUsedAt` | `DateTimeOffset?` | set | Last successful verification, or null |

Only `RevokedAt` and `LastUsedAt` mutate over a key's life; everything else is fixed at issuance.

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
4. **Expiry.** A record whose `ExpiresAt` is at or before now is `Expired`.
5. **Scope.** If `requiredScope` is supplied and the record's scopes do not contain it, the result is
   `MissingScope`.
6. **Valid.** Otherwise the result is `Valid`, and the record's `LastUsedAt` is stamped and persisted.

`ApiKeyVerification` exposes:

- `Status` - the `ApiKeyStatus` enum value.
- `IsValid` - true only when `Status == Valid`.
- `Record` - the matched record, present for `Valid`, `Expired`, `Revoked`, and `MissingScope`; null
  for `Malformed` and `NotFound`.

`ApiKeyStatus`: `Valid`, `Malformed`, `NotFound`, `Expired`, `Revoked`, `MissingScope`.

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

- `true` when an active key was found and revoked (its `RevokedAt` is stamped and persisted).
- `false` when no key has that id, or the key was already revoked.

The `false`-on-already-revoked behaviour makes repeated calls idempotent. A revoked key thereafter
verifies as `Revoked`. Revocation is permanent; there is no un-revoke.

---

## 6. Last-used tracking

A `Valid` verification stamps the record's `LastUsedAt` with the current time and persists it through
the store's `UpdateAsync`. Only successful verifications update it; rejected attempts do not. This
gives you a cheap "when was this key last actually used" signal for key hygiene and stale-key
cleanup.

---

## 7. Storage

`IApiKeyStore` is the persistence seam, four methods:

```csharp
Task AddAsync(ApiKeyRecord record, CancellationToken ct = default);
Task<ApiKeyRecord?> FindByHashAsync(string hash, CancellationToken ct = default);
Task<ApiKeyRecord?> FindByIdAsync(string id, CancellationToken ct = default);
Task UpdateAsync(ApiKeyRecord record, CancellationToken ct = default);
```

- `FindByHashAsync` is the verification hot path; the hash column should be indexed.
- `FindByIdAsync` backs revocation and administration.
- `UpdateAsync` persists the two mutable fields, `RevokedAt` and `LastUsedAt`.

`InMemoryApiKeyStore` is the bundled implementation: two `ConcurrentDictionary` indexes (by hash and
by id) over the same record instances. It is process-local, so it does not survive a restart and is
not shared across instances. It suits a single instance, tests, and getting started; use a
database-backed store for anything multi-instance or durable.

To use a custom store, register your `IApiKeyStore` before `AddOrionLedger()`; the in-memory store is
only registered if no `IApiKeyStore` is already present (`TryAddSingleton`).

---

## 8. Token generation and hashing

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

## 9. Telemetry

`ApiKeyDiagnostics` owns an OpenTelemetry `Meter` named `Moongazing.OrionLedger`
(`ApiKeyDiagnostics.MeterName`), registered as a singleton and disposable. Three counters:

| Instrument | Unit | Tags | Increments on |
|------------|------|------|---------------|
| `orionledger.keys.issued` | `{key}` | - | Each issued key |
| `orionledger.verifications` | `{verification}` | `status` | Each verification attempt |
| `orionledger.keys.revoked` | `{key}` | - | Each revoked key |

The `status` tag on `orionledger.verifications` takes one of `valid`, `malformed`, `not_found`,
`expired`, `revoked`, `missing_scope`. Subscribe with the standard metrics pipeline:

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(m => m.AddMeter(ApiKeyDiagnostics.MeterName));
```

---

## 10. Lifecycle observer

`IApiKeyEventObserver` is an optional audit hook with three callbacks:

```csharp
void OnIssued(ApiKeyRecord record);
void OnVerified(ApiKeyVerification verification);
void OnRevoked(ApiKeyRecord record);
```

Register one and it is invoked after each corresponding operation. The service treats observers as
observability, not load-bearing logic: any exception an observer throws is caught and swallowed so it
can never block issuance, verification, or revocation. When no observer is registered, an internal
no-op (`NullApiKeyEventObserver`) is used.

`OnVerified` fires for every attempt, including rejected ones, so it is the natural place to build a
full audit trail of who presented what and what the outcome was.

---

## 11. Configuration and registration

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

## 12. Targeting and build

- Multi-targets `net8.0`, `net9.0`, `net10.0`.
- Nullable reference types enabled; implicit usings enabled.
- `TreatWarningsAsErrors` with latest recommended analyzers and enforced code style.
- XML documentation is generated and shipped with the package.
- The only runtime dependency is `Microsoft.Extensions.DependencyInjection.Abstractions`.
