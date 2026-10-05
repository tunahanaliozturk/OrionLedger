# OrionLedger.AspNetCore

ASP.NET Core authentication for OrionLedger API keys: a handler reads the key from a request header, verifies it through `IApiKeyService`, and builds a `ClaimsPrincipal` whose scope claims drive standard authorization policies.

![OrionLedger overview: OrionLedger.AspNetCore sits between the API client and the core, calling VerifyAsync and handing a ClaimsPrincipal to ASP.NET Core authorization](https://raw.githubusercontent.com/tunahanaliozturk/OrionLedger/main/docs/diagrams/overview.png)

## Install

    dotnet add package OrionLedger.AspNetCore

It references `OrionLedger` and the `Microsoft.AspNetCore.App` shared framework. Register the key service with `AddOrionLedger()`; the handler resolves `IApiKeyService` from it.

## Quick start

```csharp
using Moongazing.OrionLedger;
using Moongazing.OrionLedger.AspNetCore;
using Moongazing.OrionLedger.AspNetCore.Authorization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOrionLedger();   // in-memory store unless you register another IApiKeyStore first

builder.Services
    .AddAuthentication(ApiKeyAuthenticationOptions.DefaultScheme)
    .AddOrionLedgerApiKey();

builder.Services.AddAuthorization(options =>
    options.AddApiKeyScopePolicy("orders-read", "orders:read"));

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/orders", () => "ok").RequireAuthorization("orders-read");

app.Run();
```

## How a request is handled

- The value of the `HeaderName` header is the token, used verbatim (no `Bearer` prefix is stripped).
- A missing or empty header returns `NoResult`, so other schemes and anonymous endpoints are untouched.
- A `Valid` key yields a principal with the key id, the subject (when the key has one, also the identity name) and one claim per scope. Any other status (malformed, unknown, revoked, retired, expired) fails authentication, so the framework returns `401`.
- The handler does not check scopes itself. A policy built with `RequireApiKeyScope` or `AddApiKeyScopePolicy` does, and a valid key without the scope is forbidden (`403`).
- Scope requirements only read claims from the identity of the OrionLedger scheme, so a `scope` claim from a cookie or JWT identity on the same principal cannot satisfy them.

## Options

`ApiKeyAuthenticationOptions` (scheme `OrionLedgerApiKey`, `ApiKeyAuthenticationOptions.DefaultScheme`):

- `HeaderName` - default `X-Api-Key`.
- `ScopeClaimType` - default `scope`.
- `SubjectClaimType` - default `ClaimTypes.NameIdentifier`.
- `KeyIdClaimType` - default `orionledger:key-id`.

A null or empty value fails validation when the scheme's options are built.

## Requiring scopes

```csharp
builder.Services.AddAuthorization(options =>
{
    // Any one of the listed scopes.
    options.AddPolicy("reporting", p => p.RequireApiKeyScope("reports:read", "reports:write"));

    // Every listed scope.
    options.AddPolicy("admin", p => p.RequireApiKeyScope(
        ApiKeyAuthenticationOptions.DefaultScopeClaimType, requireAll: true, "keys:admin", "keys:write"));
});
```

Overloads take an explicit scope claim type and scheme name for a handler registered under a non-default scheme.

## Related packages

- `OrionLedger` - the key lifecycle core this handler verifies against.
- `OrionLedger.EntityFrameworkCore` - durable `IApiKeyStore` over EF Core.

## Links

- Documentation and full README: https://github.com/tunahanaliozturk/OrionLedger
- Changelog: https://github.com/tunahanaliozturk/OrionLedger/blob/main/CHANGELOG.md
- License: MIT
