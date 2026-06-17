<p align="center">
  <img src="docs/logo.png" alt="OrionLedger" width="150" />
</p>

# OrionLedger

[![CI/CD](https://github.com/tunahanaliozturk/OrionLedger/actions/workflows/ci-cd.yml/badge.svg)](https://github.com/tunahanaliozturk/OrionLedger/actions/workflows/ci-cd.yml)
[![NuGet](https://img.shields.io/nuget/v/OrionLedger.svg)](https://www.nuget.org/packages/OrionLedger/)

API key lifecycle for .NET. Issue prefixed, high-entropy keys; store only their hash; verify a
presented key against prefix, hash, expiry, revocation, and scope; revoke keys; and track when each
key was last used.

Part of the **Orion** family. Usable entirely on its own.

> **Note on the name.** Despite "Ledger", this is not a financial or accounting ledger. It keeps a
> ledger of issued API keys: the set of keys that exist, their scopes, and their issue, expiry,
> revoke, and last-used state. There is no money, balance, or double-entry bookkeeping anywhere in
> the package.

## Why

Rolling your own API keys invites two classic mistakes: storing the key in a form an attacker who
reads your database can use, and treating verification as a string compare that ignores expiry,
revocation, and scope. OrionLedger generates keys the way payment providers do (a recognisable
prefix plus 256 bits of randomness), stores only a SHA-256 hash, and resolves verification to a
single status you can switch on.

## How it works

Issuance generates a plaintext token once, hashes it, and persists only the record. Verification
walks a fixed sequence of checks and collapses to a single `ApiKeyStatus`. The plaintext token
never touches storage.

```
Issue:   name + scopes -> generate(prefix + 256-bit secret) -> hash -> store record -> return token ONCE

Verify:  token -> prefix check -> hash lookup -> revoked? -> expired? -> scope? -> Valid (stamp last-used)
                     |               |             |           |          |
                  Malformed       NotFound      Revoked     Expired   MissingScope
```

Every branch except `Malformed` and `NotFound` returns the matched record, so you can log which key
was rejected and why.

## Features

- **Prefixed, high-entropy tokens.** `prefix + base64url(secret)`; 32 random bytes (256 bits) by
  default, configurable down to a 16-byte floor. The prefix makes leaked keys recognisable in logs.
- **Hash at rest.** Only the SHA-256 hash of a token is stored. The plaintext is returned once at
  issuance and is unrecoverable afterwards.
- **Single-status verification.** One call resolves prefix, hash lookup, revocation, expiry, and an
  optional required scope into one `ApiKeyStatus`.
- **Scopes.** Grant scopes at issuance; require one at verification.
- **Expiry.** Per-key explicit expiry, or a configured default lifetime, or no expiry.
- **Revocation.** Revoke by id; revoked keys verify as `Revoked`.
- **Last-used tracking.** A successful verification stamps `LastUsedAt` on the record.
- **Pluggable storage.** Ships with an in-memory store; implement `IApiKeyStore` (four methods) over
  your database to persist and share keys.
- **Telemetry and audit.** An OpenTelemetry meter plus a fault-safe lifecycle observer.
- **Constant-time hash comparison helper** for callers that compare hashes directly.
- Multi-targets `net8.0`, `net9.0`, `net10.0`; nullable enabled; warnings as errors.

A deeper breakdown lives in [docs/FEATURES.md](docs/FEATURES.md).

## Install

```
dotnet add package OrionLedger
```

## Quick start

Register the service and configure issuance:

```csharp
builder.Services.AddOrionLedger(o =>
{
    o.Prefix = "ork_live_";
    o.DefaultLifetime = TimeSpan.FromDays(90);   // optional
});
```

Issue a key. The plaintext token exists exactly once, here; store nothing but the record:

```csharp
app.MapPost("/keys", async (IApiKeyService keys) =>
{
    var issued = await keys.IssueAsync("Acme Corp", scopes: ["orders:read", "orders:write"]);
    return Results.Ok(new { apiKey = issued.Token });   // the ONLY time the token is plaintext
});
```

Verify a presented key:

```csharp
var result = await keys.VerifyAsync(presentedToken, requiredScope: "orders:write");
if (!result.IsValid)
{
    return result.Status switch
    {
        ApiKeyStatus.MissingScope => Results.StatusCode(403),
        _                         => Results.StatusCode(401),
    };
}

var tenant = result.Record!.Name;
```

## Usage

### Scopes

Pass the scopes a key should carry to `IssueAsync`, and the scope a request requires to
`VerifyAsync`. Scope matching is exact, ordinal, and case-sensitive.

```csharp
var issued = await keys.IssueAsync("reporting-job", scopes: ["reports:read"]);

var ok  = await keys.VerifyAsync(token, requiredScope: "reports:read");   // Valid
var no  = await keys.VerifyAsync(token, requiredScope: "reports:write");  // MissingScope
var any = await keys.VerifyAsync(token);                                  // skips the scope check
```

`requiredScope: null` (the default) skips the scope check entirely; the key is still validated for
prefix, existence, revocation, and expiry.

### Expiry

Expiry resolves in this order: an explicit `expiresAt` on `IssueAsync`, else the configured
`DefaultLifetime` added to the issue time, else the key never expires.

```csharp
// Explicit expiry on a single key.
await keys.IssueAsync("temp-integration", expiresAt: DateTimeOffset.UtcNow.AddHours(1));

// No explicit expiry: DefaultLifetime applies if configured, otherwise the key never expires.
await keys.IssueAsync("service-account");
```

A key past its expiry verifies as `ApiKeyStatus.Expired`, and the matched record is returned so you
can log it.

### Revocation

Revoke by the record id assigned at issuance. `RevokeAsync` returns `true` when it revoked an active
key, and `false` if no such key exists or it was already revoked (so the call is idempotent).

```csharp
var issued = await keys.IssueAsync("Acme Corp");
string keyId = issued.Record.Id;

bool revoked = await keys.RevokeAsync(keyId);   // true
bool again   = await keys.RevokeAsync(keyId);   // false (already revoked)
```

After revocation the key verifies as `ApiKeyStatus.Revoked`.

### Custom store

The default `InMemoryApiKeyStore` is process-local: it does not survive a restart and is not shared
across instances. To persist keys, implement `IApiKeyStore` over your database and register it
*before* `AddOrionLedger()`. The in-memory store is only added if no `IApiKeyStore` is already
present.

```csharp
public sealed class SqlApiKeyStore : IApiKeyStore
{
    public Task AddAsync(ApiKeyRecord record, CancellationToken ct = default) { /* INSERT */ }
    public Task<ApiKeyRecord?> FindByHashAsync(string hash, CancellationToken ct = default) { /* by hash */ }
    public Task<ApiKeyRecord?> FindByIdAsync(string id, CancellationToken ct = default) { /* by id */ }
    public Task UpdateAsync(ApiKeyRecord record, CancellationToken ct = default) { /* UPDATE */ }
}

// Registration order matters: register the store first.
builder.Services.AddSingleton<IApiKeyStore, SqlApiKeyStore>();
builder.Services.AddOrionLedger(o => o.Prefix = "ork_live_");
```

The hash is the verification lookup key; index it. `UpdateAsync` persists the only two mutable
fields, `RevokedAt` and `LastUsedAt`.

## Configuration

Configure issuance through `ApiKeyOptions` in the `AddOrionLedger` callback. Options are validated
when the service is built; an invalid value throws at startup.

| Option | Type | Default | Notes |
|--------|------|---------|-------|
| `Prefix` | `string` | `ork_` | Prepended to every token. Must not be empty. A trailing underscore is conventional. |
| `SecretByteLength` | `int` | `32` (256-bit) | Random bytes in the secret. Must be at least 16. |
| `DefaultLifetime` | `TimeSpan?` | `null` | Applied when a key is issued without an explicit expiry. Must be positive when set. `null` means keys do not expire by default. |

## Verification statuses

`VerifyAsync` returns an `ApiKeyVerification` with a `Status`, an `IsValid` shortcut (true only for
`Valid`), and the matched `Record` where one applies.

| Status | Meaning | Record returned |
|--------|---------|:---------------:|
| `Valid` | Known, active, unexpired, scope present; last-used updated | Yes |
| `Malformed` | Empty, or missing the configured prefix | No |
| `NotFound` | No key with this token hash | No |
| `Expired` | Past its expiry | Yes |
| `Revoked` | Revoked | Yes |
| `MissingScope` | Otherwise valid but lacks the required scope | Yes |

For every status except `Malformed` and `NotFound` the matched record is returned, so you can log
which key was rejected.

## Security notes

- **Hash at rest.** The plaintext token is returned once from `IssueAsync` and never stored. Storage
  holds only the SHA-256 hash (a 64-character lowercase hex digest) and a short non-secret display
  prefix. A reader of your database cannot reconstruct a usable key.
- **Why a fast hash is correct here.** API keys are full-entropy random tokens (256 bits by
  default), so there is no low-entropy secret to defend against brute force, and a slow password
  hash (bcrypt/Argon2) would only add latency to the verification hot path. SHA-256 is the right
  tool: O(1) verification, nothing to grind. Do not reuse this reasoning for user passwords.
- **Constant-time comparison.** `ApiKeyHasher.FixedTimeEquals` wraps
  `CryptographicOperations.FixedTimeEquals` so a direct hash comparison does not leak, through
  timing, how many leading characters matched. The default verification path looks keys up by hash
  in O(1) rather than comparing pairwise, so it does not depend on this; the helper is provided for
  callers that compare hashes themselves.
- **Show the token once.** `IssuedApiKey.Token` is the only point at which the plaintext exists.
  Surface it to the caller immediately and keep only `IssuedApiKey.Record`.

## Telemetry and diagnostics

OrionLedger publishes an OpenTelemetry meter named `Moongazing.OrionLedger`
(`ApiKeyDiagnostics.MeterName`) with three instruments:

| Instrument | Kind | Tags | Meaning |
|------------|------|------|---------|
| `orionledger.keys.issued` | counter | - | Keys issued |
| `orionledger.verifications` | counter | `status` | Verification attempts, tagged with the status tag (`valid`, `malformed`, `not_found`, `expired`, `revoked`, `missing_scope`) |
| `orionledger.keys.revoked` | counter | - | Keys revoked |

Subscribe with the usual OpenTelemetry metrics pipeline:

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(m => m.AddMeter(ApiKeyDiagnostics.MeterName));
```

For an audit trail, register an `IApiKeyEventObserver`; it is notified on issue, verify, and revoke.
The observer is fault-safe: an exception it throws is swallowed and never blocks the lifecycle
operation, because audit logging must not be load-bearing.

```csharp
public sealed class AuditObserver : IApiKeyEventObserver
{
    public void OnIssued(ApiKeyRecord record) { /* log issue */ }
    public void OnVerified(ApiKeyVerification verification) { /* log every attempt */ }
    public void OnRevoked(ApiKeyRecord record) { /* log revoke */ }
}

builder.Services.AddSingleton<IApiKeyEventObserver, AuditObserver>();
```

## Testing

The library is built to be tested without a database: register your own `IApiKeyStore` or use the
in-memory store directly, and construct `ApiKeyService` yourself in unit tests. The internal
constructor accepts injected clock and id factories (exposed to the test assembly), so expiry and
identifier behaviour are deterministic.

Run the suite from the repository root:

```
dotnet test
```

Benchmarks for the hot paths (generation, hashing, constant-time comparison, end-to-end lifecycle)
live in `benchmarks/Moongazing.OrionLedger.Benchmarks`. See [benchmarks.md](benchmarks.md) for what
is measured and how to run it. Results are intentionally not committed, since they depend on the
host machine.

## Versioning

OrionLedger follows semantic versioning. The package multi-targets `net8.0`, `net9.0`, and
`net10.0`. The current line is `0.1.0`; while the major version is `0`, the public surface may still
change between minor versions. Notable changes are recorded in [CHANGELOG.md](CHANGELOG.md).

## Contributing

Issues and pull requests are welcome. Please read [CONTRIBUTING.md](CONTRIBUTING.md) and the
[Code of Conduct](CODE_OF_CONDUCT.md) before opening one. Direction and ideas under consideration
are in [docs/ROADMAP.md](docs/ROADMAP.md).

## License

Licensed under the [MIT License](LICENSE).

## Author

**Tunahan Ali Ozturk** - [GitHub](https://github.com/tunahanaliozturk)
