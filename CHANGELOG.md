<!-- markdownlint-disable MD024 -->

# Changelog

All notable changes to OrionLedger are documented in this file. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.0.0/) and this project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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

[0.1.0]: https://github.com/tunahanaliozturk/OrionLedger/releases/tag/v0.1.0
