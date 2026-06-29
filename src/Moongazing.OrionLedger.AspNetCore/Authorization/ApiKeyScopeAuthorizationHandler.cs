namespace Moongazing.OrionLedger.AspNetCore.Authorization;

using System.Security.Claims;

using Microsoft.AspNetCore.Authorization;

/// <summary>
/// Evaluates <see cref="ApiKeyScopeRequirement"/> against the scope claims of the principal's
/// OrionLedger API key identity. Scope claims are read only from identities authenticated by the
/// requirement's <see cref="ApiKeyScopeRequirement.AuthenticationScheme"/>, so a scope claim minted by
/// a different scheme (a cookie or JWT identity on the same principal) cannot satisfy an API key scope
/// requirement.
/// </summary>
public sealed class ApiKeyScopeAuthorizationHandler : AuthorizationHandler<ApiKeyScopeRequirement>
{
    /// <inheritdoc />
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ApiKeyScopeRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);

        // Only the API key identity may satisfy the requirement. A ClaimsPrincipal aggregates one
        // identity per authenticated scheme, so binding to AuthenticationType (which the API key
        // handler sets to its scheme name) keeps a scope claim from another scheme out of the check.
        var held = context.User.Identities
            .Where(identity => identity.IsAuthenticated
                && string.Equals(
                    identity.AuthenticationType,
                    requirement.AuthenticationScheme,
                    StringComparison.Ordinal))
            .SelectMany(identity => identity.FindAll(requirement.ScopeClaimType))
            .Select(static claim => claim.Value);
        var heldSet = new HashSet<string>(held, StringComparer.Ordinal);

        var satisfied = requirement.RequireAll
            ? requirement.Scopes.All(heldSet.Contains)
            : requirement.Scopes.Any(heldSet.Contains);

        if (satisfied)
        {
            context.Succeed(requirement);
        }

        // Not failing explicitly: leaving the requirement unsatisfied lets another handler for the
        // same requirement (if any) still succeed, which is the ASP.NET Core convention.
        return Task.CompletedTask;
    }
}
