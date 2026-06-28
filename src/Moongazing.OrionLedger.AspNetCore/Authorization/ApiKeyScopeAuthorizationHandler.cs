namespace Moongazing.OrionLedger.AspNetCore.Authorization;

using Microsoft.AspNetCore.Authorization;

/// <summary>
/// Evaluates <see cref="ApiKeyScopeRequirement"/> against the current principal's scope claims.
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

        var held = context.User.FindAll(requirement.ScopeClaimType)
            .Select(static c => c.Value);
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
