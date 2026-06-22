# OrionLedger Roadmap

> Direction, not promises. Milestones and dates are targets that may move, and anything past the
> current release may be reshaped, reordered, or dropped. While the major version is `0` the public
> surface can still change between minor versions.

OrionLedger is at `0.2.1`: an API key lifecycle library that issues prefixed, high-entropy tokens,
stores only their hash, and verifies a presented key against prefix, hash, expiry, revocation, and
scope, with rotation, bulk revoke by subject, last-verified tracking, telemetry, and a fault-safe
observer.

If one of the directions below matters for your use case, open an issue. Concrete demand is the best
signal for what to build next.

---

## Released

### 0.2.1 (2026-06-20) - allocation-free hot path

- `ApiKeyHasher.Hash` encodes the token and formats the digest through a stack or pooled buffer,
  removing the intermediate UTF-8 `byte[]` and the double string allocation; uses
  `Convert.ToHexStringLower` on net9/net10. Same SHA-256 digest, same 64-char lowercase hex, about
  60% fewer bytes allocated per hash.
- `ApiKeyGenerator.Generate` fills and base64url-encodes secret bytes in a stack or pooled buffer,
  removing the per-issue `byte[]` and intermediate string; uses `Base64Url.EncodeToString` on
  net9/net10. The emitted token is byte-for-byte identical.
- The constant-time hash comparison in verification is unchanged.

### 0.2.0 (2026-06-19) - lifecycle additions

- Rotation: `RotateAsync` issues a fresh successor for the same logical key, inheriting name,
  subject, scopes, and expiry, with an optional grace window during which the old token still
  verifies before it resolves as `Retired`.
- Last-verified tracking: a successful verify stamps `LastUsedAt` and increments `LastUsedCount`.
- Bulk revoke: `RevokeAllForSubjectAsync` revokes every active key for a subject in one call (needs
  the optional `IApiKeyStore.FindBySubjectAsync` override).

### 0.1.0 (2026-06-14) - initial release

- Issue prefixed, high-entropy tokens (256-bit default, 16-byte floor); plaintext returned once.
- SHA-256 hash at rest; single-status verification over prefix, hash lookup, revocation, expiry, and
  an optional scope.
- `IApiKeyStore` seam with a bundled `InMemoryApiKeyStore`, OpenTelemetry meter, and a fault-safe
  `IApiKeyEventObserver`.
- `net8.0` / `net9.0` / `net10.0`.

---

## Next

### 0.3.0 - durable storage (target 2026-07)

The default store is in-memory: it does not survive a restart and is not shared across instances.
The gap most consumers hit first is persistence.

- A reference EF Core `IApiKeyStore` (a companion package, `OrionLedger.EntityFrameworkCore`) that
  maps `ApiKeyRecord`, indexes the hash as the verification lookup key, and persists the mutable
  lifecycle fields (`RevokedAt`, `LastUsedAt`, `LastUsedCount`, and the rotation timestamps). Ships
  with `FindBySubjectAsync` so bulk revoke works out of the box.
- A store contract test suite a custom `IApiKeyStore` can run against to confirm it honours
  lookup-by-hash, lookup-by-id, update, and (where supported) lookup-by-subject semantics.

### 0.4.0 - ASP.NET Core integration (target 2026-08)

Today consumers wire the header read and the `VerifyAsync` call themselves. An authentication handler
removes that boilerplate for the common case.

- An `AuthenticationHandler` that reads a key from a configurable header, calls `VerifyAsync`, and
  maps the `ApiKeyStatus` to an authentication result, surfacing the matched record as claims.
- A scope-to-policy mapping so an authorization policy can require a scope without the endpoint
  re-checking it.
- Multi-scope checks (require all / require any) beyond today's single `requiredScope`, used by both
  the handler and direct callers.

### 0.5.0 - hashing and secrets options (target 2026-09)

SHA-256 is the right default for full-entropy random tokens. Some consumers have a compliance
requirement to substitute the digest or add a server-side secret.

- Pluggable hashing behind the existing `ApiKeyHasher` seam, so the digest can be swapped without
  forking the verification path.
- Optional pepper / keyed hashing (for example HMAC-SHA-256 with a configured key) for defence in
  depth on top of hash at rest, with guidance on key rotation for the pepper itself.
- An Argon2 option for consumers whose policy mandates a slow hash, documented with its latency
  cost on the verify path so the trade-off is explicit.

---

## Under consideration

Not tied to a milestone yet; demand will decide whether and when these land.

- Administration queries: listing and pagination of keys for a `Name` or `subject`, and a
  "find expired" query to support cleanup jobs, once the EF Core store gives them a real backing.
- Rotation-reminder hooks: an observer or query surface that flags keys approaching expiry, so a host
  can prompt rotation before a key lapses.
- Per-key usage metering and rate-limit signals built on the existing last-used counters, for hosts
  that want to throttle or report per key.
- Tracing spans (an `ActivitySource`) around issue and verify to complement the metrics, plus example
  dashboards and queries for the published meter.

---

## Out of scope

- Anything implying this is a financial or accounting ledger. OrionLedger tracks API keys; it will
  not grow money, balances, or bookkeeping.
- A hosted service or key-management server. OrionLedger is a library you embed; storage and
  transport stay yours.

---

## Contributing to the roadmap

Open an issue describing the problem you are trying to solve, not only the feature you have in mind.
Concrete use cases move items up this list faster than anything else.
