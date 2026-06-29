<!-- markdownlint-disable MD024 -->

# Changelog

All notable changes to OrionLedger are documented in this file. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.0.0/) and this project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.4.0] - 2026-06-28

### Added

- New package `OrionLedger.AspNetCore`: an ASP.NET Core authentication handler that reads the API key from a configurable request header (default `X-Api-Key`), verifies it through `IApiKeyService` (hash lookup, revoked / expired / retired checks, and the last-used stamp), and on a `Valid` result establishes a `ClaimsPrincipal` carrying the key's id, subject, and one claim per granted scope. Any other status fails authentication with no principal, so the framework returns `401`; a missing header yields `NoResult` so other schemes and anonymous endpoints are undisturbed.
- Scope-to-policy mapping: `ApiKeyScopeRequirement` and its authorization handler project the verified key's scope claims into standard ASP.NET Core authorization, so `[Authorize(Policy = ...)]` can gate an endpoint on a scope without the endpoint re-checking it. Helpers `RequireApiKeyScope` (on `AuthorizationPolicyBuilder`, require-any or require-all) and `AddApiKeyScopePolicy` (on `AuthorizationOptions`) build scope policies.
- Registration extension `AddAuthentication().AddOrionLedgerApiKey(...)`, consistent with ASP.NET Core auth conventions, which adds the scheme and the scope authorization handler. Header name and the subject / key-id / scope claim types are configurable on `ApiKeyAuthenticationOptions`. The package references the `Microsoft.AspNetCore.App` shared framework and multi-targets `net8.0` / `net9.0` / `net10.0`.

### Tests

- A valid key authenticates and the principal carries the subject and scopes; revoked, expired, and unknown keys are rejected with no principal; last-used is updated on a successful verify; a custom header name is honoured. End-to-end through a `TestServer` pipeline: a scope-protected endpoint allows a key holding the scope (`200`), forbids a valid key without it (`403`), and rejects a revoked or absent key (`401`). The scope authorization handler is covered for require-any and require-all. All run against an in-memory `IApiKeyStore` and the real `ApiKeyService`.

## [0.3.0] - 2026-06-22

### Added

- New package `OrionLedger.EntityFrameworkCore`: a reference `IApiKeyStore` over Entity Framework Core. It maps `ApiKeyRecord` through `ApiKeyRecordConfiguration` (and a ready-made `OrionLedgerDbContext`), uniquely indexes the token hash as the verification lookup key, indexes the subject for bulk revoke, stores the scope set as a JSON column with a value comparer, and persists every mutable lifecycle field (`RevokedAt`, `LastUsedAt`, `LastUsedCount`, `SupersededAt`, `SupersededById`, `RetiresAt`). It ships `FindBySubjectAsync`, so `RevokeAllForSubjectAsync` works out of the box. The store depends only on `Microsoft.EntityFrameworkCore.Relational`, leaving provider choice to the consumer, and registers through `AddOrionLedgerEntityFrameworkCoreStore<TContext>()`.
- The EF store applies the last-used update as a server-side `LastUsedCount = LastUsedCount + delta` (`ExecuteUpdate`), so concurrent verifications no longer lose counts: `LastUsedCount` is exact under this store, not best-effort. The remaining lifecycle fields are last-writer-wins, matching their idempotent intent.
- New package `OrionLedger.Conformance`: a reusable xUnit contract suite (`ApiKeyStoreConformanceTests`) any custom `IApiKeyStore` can run against by supplying a store factory. It covers add, lookup by hash and id (hit and miss), update of the lifecycle fields, revocation, rotation timestamps, last-used tracking (including that concurrent increments are not lost), scope round-trips, and lookup by subject.

### Tests

- The EF store runs through the full `OrionLedger.Conformance` suite against a real SQLite database (not the EF InMemory provider), plus EF-specific tests for the unique hash constraint, durability across a fresh context, JSON scope storage, an atomic concurrent-increment check (100 parallel verifies land an exact count), and an end-to-end lifecycle wired through `ApiKeyService` and the DI extension.

## [0.2.1] - 2026-06-20

### Performance
- Key hashing (`ApiKeyHasher.Hash`) on the per-request verify path now encodes the token into a stack (or pooled) buffer and formats the digest into a single lowercase-hex string, removing the intermediate UTF-8 `byte[]` and the uppercase-then-lowercase double string allocation. On net9/net10 it uses `Convert.ToHexStringLower`. Behavior is identical (same SHA-256 digest, same 64-char lowercase hex). Measured ~60% fewer bytes allocated per hash.
- Token generation (`ApiKeyGenerator.Generate`) fills random secret bytes into a stack (or pooled) buffer and base64url-encodes them in place, removing the per-issue `byte[]` and the intermediate base64 string plus `TrimEnd`/`Replace` allocations. On net9/net10 it uses `Base64Url.EncodeToString`. The emitted token is byte-for-byte identical to before.
- The fixed-time hash comparison in verification is unchanged and remains constant-time.

## [0.2.0] - 2026-06-19

### Added
- Key lifecycle management: rotate a key (issue a fresh secret for the same logical key, with an optional grace window where the old secret still verifies before retiring), last-verified tracking updated on each successful verify, and bulk revoke of every key for a subject.

## [0.1.0] - 2026-06-14

### Added

Initial release. API key lifecycle.

- `IApiKeyService` / `ApiKeyService`: issue prefixed high-entropy keys (plaintext returned once),
  verify presented tokens against expiry, revocation, and scope, and revoke by id. Verification
  updates the last-used timestamp.
- `ApiKeyGenerator` and `ApiKeyHasher`: URL-safe random token generation and SHA-256 hashing with
  a fixed-time comparison helper. Only the hash is stored.
- `IApiKeyStore` with an in-process `InMemoryApiKeyStore` indexed by hash and id; swap in a
  database-backed store for persistence and multi-instance sharing.
- `ApiKeyVerification` with an `ApiKeyStatus` (valid/malformed/not_found/expired/revoked/
  missing_scope) and the matched record where applicable.
- `ApiKeyOptions`: prefix, secret length, default lifetime; validated on registration.
- `IApiKeyEventObserver`: fault-safe issue/verify/revoke hook for audit.
- `ApiKeyDiagnostics`: `Moongazing.OrionLedger` meter with issued, verification, and revoked
  counters.
- `AddOrionLedger()` DI extension.

### Tests

20 tests across the token and hash primitives, the service (issue, verify, expiry, default
lifetime, revoke, scope, hash-at-rest), and registration.

[0.4.0]: https://github.com/tunahanaliozturk/OrionLedger/releases/tag/v0.4.0
[0.3.0]: https://github.com/tunahanaliozturk/OrionLedger/releases/tag/v0.3.0
[0.2.1]: https://github.com/tunahanaliozturk/OrionLedger/releases/tag/v0.2.1
[0.2.0]: https://github.com/tunahanaliozturk/OrionLedger/releases/tag/v0.2.0
[0.1.0]: https://github.com/tunahanaliozturk/OrionLedger/releases/tag/v0.1.0
