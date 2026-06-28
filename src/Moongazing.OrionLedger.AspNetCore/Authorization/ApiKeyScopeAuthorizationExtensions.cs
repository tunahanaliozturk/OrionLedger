namespace Moongazing.OrionLedger.AspNetCore.Authorization;

using Microsoft.AspNetCore.Authorization;

/// <summary>
/// Helpers for requiring OrionLedger API key scopes in authorization policies, so a policy can gate
/// an endpoint on a scope without the endpoint re-checking it.
/// </summary>
public static class ApiKeyScopeAuthorizationExtensions
{
    /// <summary>
    /// Require the principal to hold any one of <paramref name="scopes"/>, matched against the scope
    /// claims emitted by the API key handler. Use the explicit overload to require all of them.
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
    /// Require API key scopes with an explicit claim type and all/any mode.
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
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(scopes);

        builder.AddRequirements(new ApiKeyScopeRequirement(scopes, scopeClaimType, requireAll));
        return builder;
    }

    /// <summary>
    /// Add a named authorization policy that requires the given scopes. The policy can then be used
    /// with <c>[Authorize(Policy = name)]</c> or <c>RequireAuthorization(name)</c>.
    /// </summary>
    /// <param name="options">The authorization options.</param>
    /// <param name="policyName">The policy name.</param>
    /// <param name="scopes">The scopes the policy requires (any one). Must be non-empty.</param>
    public static void AddApiKeyScopePolicy(
        this AuthorizationOptions options,
        string policyName,
        params string[] scopes)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrEmpty(policyName);

        options.AddPolicy(policyName, policy => policy.RequireApiKeyScope(scopes));
    }
}
