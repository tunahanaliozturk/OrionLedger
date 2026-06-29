namespace Moongazing.OrionLedger.AspNetCore;

using System.Security.Claims;

using Microsoft.AspNetCore.Authentication;

using Moongazing.OrionLedger.Keys;

/// <summary>
/// Options for the OrionLedger API key authentication handler: where to read the key from and how
/// the verified key's record is projected into a <see cref="ClaimsPrincipal"/>.
/// </summary>
public sealed class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
    /// <summary>The default scheme name registered by <c>AddOrionLedgerApiKey</c>.</summary>
    public const string DefaultScheme = "OrionLedgerApiKey";

    /// <summary>The default request header the key is read from.</summary>
    public const string DefaultHeaderName = "X-Api-Key";

    /// <summary>The default claim type each scope is emitted as.</summary>
    public const string DefaultScopeClaimType = "scope";

    /// <summary>
    /// The request header the API key is read from. Default <c>X-Api-Key</c>. The header value is
    /// used verbatim as the presented token; no <c>Bearer</c> or other prefix is stripped.
    /// </summary>
    public string HeaderName { get; set; } = DefaultHeaderName;

    /// <summary>
    /// The claim type emitted for each scope the verified key holds. Default <c>scope</c>. One claim
    /// of this type is added per scope, which is what <c>RequireApiKeyScope</c> policies match
    /// against.
    /// </summary>
    public string ScopeClaimType { get; set; } = DefaultScopeClaimType;

    /// <summary>
    /// The claim type the verified key's <see cref="ApiKeyRecord.Subject"/> is emitted as, and which
    /// becomes the principal's <see cref="ClaimsIdentity.Name"/>. Default
    /// <see cref="ClaimTypes.NameIdentifier"/>. The subject claim is only added when the key carries a
    /// subject.
    /// </summary>
    public string SubjectClaimType { get; set; } = ClaimTypes.NameIdentifier;

    /// <summary>
    /// The claim type the verified key's <see cref="ApiKeyRecord.Id"/> is emitted as. Default
    /// <c>orionledger:key-id</c>. Always added on success so a host can attribute a request to the
    /// exact key, independent of subject.
    /// </summary>
    public string KeyIdClaimType { get; set; } = "orionledger:key-id";

    /// <summary>
    /// Validate the configured header and claim types. Called by the framework when the scheme's
    /// options are first built, so a blank value fails fast at startup.
    /// </summary>
    public override void Validate()
    {
        base.Validate();
        ArgumentException.ThrowIfNullOrEmpty(HeaderName);
        ArgumentException.ThrowIfNullOrEmpty(ScopeClaimType);
        ArgumentException.ThrowIfNullOrEmpty(SubjectClaimType);
        ArgumentException.ThrowIfNullOrEmpty(KeyIdClaimType);
    }
}
