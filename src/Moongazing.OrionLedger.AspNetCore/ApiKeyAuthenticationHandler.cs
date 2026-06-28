namespace Moongazing.OrionLedger.AspNetCore;

using System.Security.Claims;
using System.Text.Encodings.Web;

using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Moongazing.OrionLedger.Keys;

/// <summary>
/// Authenticates a request by reading an API key from a configured header and verifying it through
/// <see cref="IApiKeyService"/>. On a <see cref="ApiKeyStatus.Valid"/> result it builds a
/// <see cref="ClaimsPrincipal"/> carrying the key's subject, id, and scopes; any other status fails
/// authentication with no principal, so the request is treated as unauthenticated.
/// </summary>
public sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyAuthenticationOptions>
{
    private readonly IApiKeyService apiKeyService;

    /// <summary>Create the handler. The framework resolves the dependencies.</summary>
    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<ApiKeyAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IApiKeyService apiKeyService)
        : base(options, logger, encoder)
    {
        ArgumentNullException.ThrowIfNull(apiKeyService);
        this.apiKeyService = apiKeyService;
    }

    /// <inheritdoc />
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(Options.HeaderName, out var values))
        {
            // No credentials presented. NoResult (not Fail) so other schemes still get a turn and an
            // anonymous endpoint is not disturbed.
            return AuthenticateResult.NoResult();
        }

        var token = values.ToString();
        if (string.IsNullOrEmpty(token))
        {
            return AuthenticateResult.NoResult();
        }

        var verification = await apiKeyService
            .VerifyAsync(token, requiredScope: null, Context.RequestAborted)
            .ConfigureAwait(false);

        if (!verification.IsValid || verification.Record is null)
        {
            // Revoked, expired, retired, unknown, or malformed: no principal. The scope check is left
            // to authorization, so the handler does not pass a requiredScope here.
            return AuthenticateResult.Fail($"API key rejected: {verification.Status}.");
        }

        var ticket = BuildTicket(verification.Record);
        return AuthenticateResult.Success(ticket);
    }

    private AuthenticationTicket BuildTicket(ApiKeyRecord record)
    {
        var claims = new List<Claim>
        {
            new(Options.KeyIdClaimType, record.Id),
        };

        if (!string.IsNullOrEmpty(record.Subject))
        {
            claims.Add(new Claim(Options.SubjectClaimType, record.Subject));
        }

        foreach (var scope in record.Scopes)
        {
            claims.Add(new Claim(Options.ScopeClaimType, scope));
        }

        // nameType is the subject claim so HttpContext.User.Identity.Name resolves to the key owner
        // when one is present; roleType is left at its default.
        var identity = new ClaimsIdentity(claims, Scheme.Name, Options.SubjectClaimType, ClaimTypes.Role);
        var principal = new ClaimsPrincipal(identity);
        return new AuthenticationTicket(principal, Scheme.Name);
    }
}
