namespace Moongazing.OrionLedger.AspNetCore;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Moongazing.OrionLedger.AspNetCore.Authorization;

/// <summary>
/// Registration helpers for OrionLedger API key authentication, consistent with the ASP.NET Core
/// <c>AddAuthentication().Add{Scheme}()</c> convention.
/// </summary>
public static class OrionLedgerAuthenticationBuilderExtensions
{
    /// <summary>
    /// Add the OrionLedger API key authentication scheme under
    /// <see cref="ApiKeyAuthenticationOptions.DefaultScheme"/>.
    /// </summary>
    /// <remarks>
    /// The handler verifies a presented key through <see cref="IApiKeyService"/>, which the host must
    /// register separately (for example with <c>AddOrionLedger()</c>). This call also registers the
    /// <see cref="ApiKeyScopeAuthorizationHandler"/> so scope policies built with
    /// <c>RequireApiKeyScope</c> resolve.
    /// </remarks>
    /// <param name="builder">The authentication builder.</param>
    /// <param name="configureOptions">Optional handler configuration (header name, claim types).</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static AuthenticationBuilder AddOrionLedgerApiKey(
        this AuthenticationBuilder builder,
        Action<ApiKeyAuthenticationOptions>? configureOptions = null)
        => builder.AddOrionLedgerApiKey(
            ApiKeyAuthenticationOptions.DefaultScheme,
            displayName: null,
            configureOptions);

    /// <summary>
    /// Add the OrionLedger API key authentication scheme under an explicit scheme name.
    /// </summary>
    /// <param name="builder">The authentication builder.</param>
    /// <param name="authenticationScheme">The scheme name.</param>
    /// <param name="configureOptions">Optional handler configuration.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static AuthenticationBuilder AddOrionLedgerApiKey(
        this AuthenticationBuilder builder,
        string authenticationScheme,
        Action<ApiKeyAuthenticationOptions>? configureOptions)
        => builder.AddOrionLedgerApiKey(authenticationScheme, displayName: null, configureOptions);

    /// <summary>
    /// Add the OrionLedger API key authentication scheme under an explicit scheme name and display
    /// name.
    /// </summary>
    /// <param name="builder">The authentication builder.</param>
    /// <param name="authenticationScheme">The scheme name.</param>
    /// <param name="displayName">The scheme display name, or null.</param>
    /// <param name="configureOptions">Optional handler configuration.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static AuthenticationBuilder AddOrionLedgerApiKey(
        this AuthenticationBuilder builder,
        string authenticationScheme,
        string? displayName,
        Action<ApiKeyAuthenticationOptions>? configureOptions)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(authenticationScheme);

        // The scope authorization handler backs RequireApiKeyScope policies. Registered idempotently
        // so repeated AddOrionLedgerApiKey calls (for example multiple schemes) add it only once.
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IAuthorizationHandler, ApiKeyScopeAuthorizationHandler>());

        // AddScheme wires ApiKeyAuthenticationOptions.Validate() as the scheme's validation hook, so
        // a blank header or claim type is caught when the handler initializes.
        return builder.AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
            authenticationScheme,
            displayName,
            configureOptions);
    }
}
