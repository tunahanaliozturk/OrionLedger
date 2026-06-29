namespace Moongazing.OrionLedger.AspNetCore.Authorization;

using Microsoft.AspNetCore.Authorization;

/// <summary>
/// Helpers for requiring OrionLedger API key scopes in authorization policies, so a policy can gate
/// an endpoint on a scope without the endpoint re-checking it. Every requirement these helpers add is
/// bound to the OrionLedger API key scheme: only an identity authenticated by that scheme can satisfy
/// it, so a scope claim from another scheme on the same principal does not.
/// </summary>
public static class ApiKeyScopeAuthorizationExtensions
{
    /// <summary>
    /// Require the principal's OrionLedger API key identity to hold any one of <paramref name="scopes"/>,
    /// matched against the scope claims emitted by the API key handler under its default scheme and
    /// claim type. Use the explicit overload to require all of them, or to bind a custom claim type or
    /// scheme.
    /// </summary>
    /// <param name="builder">The policy builder.</param>
    /// <param name="scopes">The scopes to require. Must be non-empty.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static AuthorizationPolicyBuilder RequireApiKeyScope(
        this AuthorizationPolicyBuilder builder,
        params string[] scopes)
        => builder.RequireApiKeyScope(
            ApiKeyAuthenticationOptions.DefaultScopeClaimType,
            requireAll: false,
            scopes);

    /// <summary>
    /// Require API key scopes with an explicit claim type and all/any mode, bound to the default
    /// OrionLedger API key scheme.
    /// </summary>
    /// <param name="builder">The policy builder.</param>
    /// <param name="scopeClaimType">The claim type scopes are emitted as.</param>
    /// <param name="requireAll">True to require every scope; false to require any one.</param>
    /// <param name="scopes">The scopes to require. Must be non-empty.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static AuthorizationPolicyBuilder RequireApiKeyScope(
        this AuthorizationPolicyBuilder builder,
        string scopeClaimType,
        bool requireAll,
        params string[] scopes)
        => builder.RequireApiKeyScope(
            scopeClaimType,
            requireAll,
            ApiKeyAuthenticationOptions.DefaultScheme,
            scopes);

    /// <summary>
    /// Require API key scopes with an explicit claim type, all/any mode, and a bound authentication
    /// scheme. Pass <paramref name="authenticationScheme"/> when the API key handler is registered
    /// under a non-default scheme so the requirement reads scope claims from that scheme's identity.
    /// </summary>
    /// <param name="builder">The policy builder.</param>
    /// <param name="scopeClaimType">The claim type scopes are emitted as.</param>
    /// <param name="requireAll">True to require every scope; false to require any one.</param>
    /// <param name="authenticationScheme">
    /// The scheme whose identity may satisfy the requirement. Must match the scheme the API key handler
    /// is registered under and the configured scope claim type for the policy to resolve.
    /// </param>
    /// <param name="scopes">The scopes to require. Must be non-empty.</param>
    /// <returns>The same <paramref name="builder"/> for chaining.</returns>
    public static AuthorizationPolicyBuilder RequireApiKeyScope(
        this AuthorizationPolicyBuilder builder,
        string scopeClaimType,
        bool requireAll,
        string authenticationScheme,
        params string[] scopes)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(scopes);

        builder.AddRequirements(
            new ApiKeyScopeRequirement(scopes, scopeClaimType, requireAll, authenticationScheme));
        return builder;
    }

    /// <summary>
    /// Add a named authorization policy that requires the given scopes (any one), bound to the default
    /// OrionLedger API key scheme. The policy can then be used with <c>[Authorize(Policy = name)]</c>
    /// or <c>RequireAuthorization(name)</c>.
    /// </summary>
    /// <param name="options">The authorization options.</param>
    /// <param name="policyName">The policy name.</param>
    /// <param name="scopes">The scopes the policy requires (any one). Must be non-empty.</param>
    public static void AddApiKeyScopePolicy(
        this AuthorizationOptions options,
        string policyName,
        params string[] scopes)
        => options.AddApiKeyScopePolicy(
            policyName,
            ApiKeyAuthenticationOptions.DefaultScopeClaimType,
            ApiKeyAuthenticationOptions.DefaultScheme,
            scopes);

    /// <summary>
    /// Add a named authorization policy that requires the given scopes (any one), with an explicit
    /// scope claim type and bound authentication scheme. Use this overload when the API key handler is
    /// registered under a non-default scheme or emits scopes under a non-default claim type.
    /// </summary>
    /// <param name="options">The authorization options.</param>
    /// <param name="policyName">The policy name.</param>
    /// <param name="scopeClaimType">The claim type scopes are emitted as.</param>
    /// <param name="authenticationScheme">The scheme whose identity may satisfy the policy.</param>
    /// <param name="scopes">The scopes the policy requires (any one). Must be non-empty.</param>
    public static void AddApiKeyScopePolicy(
        this AuthorizationOptions options,
        string policyName,
        string scopeClaimType,
        string authenticationScheme,
        params string[] scopes)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrEmpty(policyName);

        options.AddPolicy(
            policyName,
            policy => policy.RequireApiKeyScope(
                scopeClaimType,
                requireAll: false,
                authenticationScheme,
                scopes));
    }
}
