<p align="center">
  <img src="https://raw.githubusercontent.com/tunahanaliozturk/OrionLedger/main/docs/logo.png" alt="OrionLedger" width="150" />
</p>

# OrionLedger.AspNetCore

[![NuGet](https://img.shields.io/nuget/v/OrionLedger.AspNetCore.svg)](https://www.nuget.org/packages/OrionLedger.AspNetCore/)

ASP.NET Core authentication for [OrionLedger](https://www.nuget.org/packages/OrionLedger). An
authentication handler reads the API key from a configurable request header, verifies it through
OrionLedger (hash lookup, revoked / expired / retired checks, and the last-used stamp), and on
success establishes a `ClaimsPrincipal` carrying the key's subject, id, and scopes. Scopes are
projected into claims so standard `[Authorize(Policy = ...)]` authorization gates an endpoint on a
scope without the endpoint re-checking it.

```bash
dotnet add package OrionLedger.AspNetCore
```

## Quick start

```csharp
using Moongazing.OrionLedger;
using Moongazing.OrionLedger.AspNetCore;
using Moongazing.OrionLedger.AspNetCore.Authorization;

var builder = WebApplication.CreateBuilder(args);

// The key service the handler verifies against (in-memory store by default; swap in the
// EF Core store for persistence).
builder.Services.AddOrionLedger();

builder.Services
    .AddAuthentication(ApiKeyAuthenticationOptions.DefaultScheme)
    .AddOrionLedgerApiKey(options =>
    {
        options.HeaderName = "X-Api-Key"; // the default
    });

builder.Services.AddAuthorization(options =>
{
    // A named policy that requires the "orders:read" scope.
    options.AddApiKeyScopePolicy("orders:read", "orders:read");
});

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/orders", () => "ok")
   .RequireAuthorization("orders:read");

app.Run();
```

## How it maps

- The header value is used verbatim as the presented token (no `Bearer` prefix is stripped).
- A `Valid` verification yields a principal with: the key id (`orionledger:key-id` by default), the
  subject as `ClaimTypes.NameIdentifier` and the identity name (when the key has a subject), and one
  `scope` claim per granted scope.
- Any other status (revoked, expired, retired, unknown, malformed) fails authentication with no
  principal, so the request is unauthenticated and the framework returns `401`.
- A missing header produces `NoResult`, leaving other schemes and anonymous endpoints undisturbed.

## Requiring scopes

Inline on an endpoint or in a named policy:

```csharp
builder.Services.AddAuthorization(options =>
{
    // Any one of the listed scopes.
    options.AddPolicy("reporting", p => p.RequireApiKeyScope("reports:read", "reports:write"));

    // Require all listed scopes.
    options.AddPolicy("admin", p => p.RequireApiKeyScope(
        ApiKeyAuthenticationOptions.DefaultScopeClaimType, requireAll: true, "keys:admin", "keys:write"));
});
```

The claim type used for scopes, the subject, and the key id are all configurable on
`ApiKeyAuthenticationOptions`.

See the [project repository](https://github.com/tunahanaliozturk/OrionLedger) for the core key
lifecycle API, the EF Core store, and the full design spec.
