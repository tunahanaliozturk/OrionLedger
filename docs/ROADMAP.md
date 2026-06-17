# OrionLedger Roadmap

> Ideas under consideration, not promises. Nothing here is committed, and there are no dates.

OrionLedger is at `0.1.0`: the core API key lifecycle (issue, verify, scopes, expiry, revocation,
last-used, pluggable storage, telemetry, fault-safe observer) is in place. This document lists
directions that seem worth exploring next. Order does not imply priority, and anything here may be
dropped, reshaped, or shipped differently.

If one of these matters for your use case, open an issue. Real demand is the best signal for what to
build next.

---

## Shipped (today)

- Prefixed, high-entropy token generation with configurable entropy (256-bit default, 16-byte floor).
- SHA-256 hash-at-rest; plaintext returned once at issuance.
- Single-status verification over prefix, hash lookup, revocation, expiry, and an optional scope.
- Scopes, per-key and default-lifetime expiry, idempotent revocation, last-used tracking.
- `IApiKeyStore` seam with a bundled in-memory store.
- OpenTelemetry meter and a fault-safe `IApiKeyEventObserver`.
- `net8.0` / `net9.0` / `net10.0`.

---

## Ideas under consideration

### Storage

- A reference EF Core store implementation (or a companion package), so persistence is not
  hand-rolled for every consumer.
- A guide and contract tests that a custom `IApiKeyStore` can run against to confirm it honours the
  lookup-by-hash, lookup-by-id, and update semantics.

### Key lifecycle

- Key rotation helpers: issue a successor and overlap a grace window before the predecessor stops
  verifying.
- Listing and pagination of keys for a given `Name` (administration), which today depends entirely on
  the store implementation.
- Optional automatic expiry sweeps or a "find expired" query to support cleanup jobs.

### Verification ergonomics

- An optional ASP.NET Core authentication handler or middleware that reads a key from a header and
  surfaces the `ApiKeyVerification` as the result, so consumers do not wire the plumbing themselves.
- Multi-scope checks (require all / require any) beyond today's single `requiredScope`.

### Hashing and secrets

- Pluggable hashing, so a consumer with a specific compliance requirement can substitute the digest
  without forking the verification path.
- Optional pepper / keyed hashing for defence in depth on top of hash-at-rest.

### Observability

- Tracing spans (an `ActivitySource`) around issue and verify, to complement the existing metrics.
- A small set of recommended dashboards or example queries for the published meter.

---

## Out of scope

- Anything implying this is a financial or accounting ledger. OrionLedger tracks API keys; it will
  not grow money, balances, or bookkeeping.
- A hosted service or key-management server. OrionLedger is a library you embed; storage and
  transport stay yours.

---

## Contributing to the roadmap

Open an issue describing the problem you are trying to solve (not only the feature you have in mind).
Concrete use cases move ideas up this list faster than anything else.
