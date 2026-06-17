<p align="center">
  <img src="docs/logo.png" alt="OrionLedger" width="150" />
</p>

# OrionLedger

[![CI/CD](https://github.com/tunahanaliozturk/OrionLedger/actions/workflows/ci-cd.yml/badge.svg)](https://github.com/tunahanaliozturk/OrionLedger/actions/workflows/ci-cd.yml)
[![NuGet](https://img.shields.io/nuget/v/OrionLedger.svg)](https://www.nuget.org/packages/OrionLedger/)

API key lifecycle for .NET. Issue prefixed, high-entropy keys; store only their hash; verify a
presented key against scope, expiry, and revocation; and track when each key was last used.

Part of the **Orion** family. Usable entirely on its own.

## Why

Rolling your own API keys invites two classic mistakes: storing the key in a form an attacker who
reads your database can use, and treating verification as a string compare that ignores expiry,
revocation, and scope. OrionLedger generates keys the way payment providers do (a recognisable
prefix plus 256 bits of randomness), stores only a SHA-256 hash, and resolves verification to a
single status you can switch on.

## Install

```
dotnet add package OrionLedger
```

## Quick start

```csharp
builder.Services.AddOrionLedger(o =>
{
    o.Prefix = "ork_live_";
    o.DefaultLifetime = TimeSpan.FromDays(90);   // optional
});
```

Issue a key (show the plaintext once, store nothing but the record):

```csharp
var issued = await keys.IssueAsync("Acme Corp", scopes: ["orders:read", "orders:write"]);
return Results.Ok(new { apiKey = issued.Token });   // the ONLY time the token exists in plaintext
```

Verify a presented key:

```csharp
var result = await keys.VerifyAsync(presentedToken, requiredScope: "orders:write");
if (!result.IsValid)
{
    return result.Status switch
    {
        ApiKeyStatus.Expired      => Results.StatusCode(401),
        ApiKeyStatus.Revoked      => Results.StatusCode(401),
        ApiKeyStatus.MissingScope => Results.StatusCode(403),
        _                         => Results.StatusCode(401),
    };
}

var tenant = result.Record!.Name;
```

Revoke a key:

```csharp
await keys.RevokeAsync(keyId);
```

## Verification statuses

| Status | Meaning |
|--------|---------|
| `Valid` | Known, active, unexpired, scope present; last-used updated |
| `Malformed` | Empty, or missing the configured prefix |
| `NotFound` | No key with this token hash |
| `Expired` | Past its expiry |
| `Revoked` | Revoked |
| `MissingScope` | Otherwise valid but lacks the required scope |

For every status except `Malformed` and `NotFound` the matched record is returned, so you can log
which key was rejected.

## Storage

The default `InMemoryApiKeyStore` is process-local. To persist keys and share them across
instances, implement `IApiKeyStore` (four methods: add, find-by-hash, find-by-id, update) over
your database and register it before `AddOrionLedger()`; the in-memory store is only added if none
is present.

## Telemetry and audit

Subscribe to the `Moongazing.OrionLedger` meter: `orionledger.keys.issued`,
`orionledger.verifications` (tagged `status`), and `orionledger.keys.revoked`. For an audit trail,
register an `IApiKeyEventObserver` to be notified on issue, verify, and revoke. The observer is
fault-safe: an exception it throws never blocks the lifecycle operation.

## Design

- Multi-targets `net8.0`, `net9.0`, `net10.0`.
- `TreatWarningsAsErrors`, latest analyzers, nullable enabled.
- Keys are 256-bit random by default; only their SHA-256 hash is stored. Hash comparison uses a
  fixed-time helper.

## License

MIT.
