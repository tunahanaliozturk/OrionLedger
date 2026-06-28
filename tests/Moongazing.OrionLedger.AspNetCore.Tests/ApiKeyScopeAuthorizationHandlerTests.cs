namespace Moongazing.OrionLedger.AspNetCore.Tests;

using System.Security.Claims;

using Microsoft.AspNetCore.Authorization;

using Moongazing.OrionLedger.AspNetCore.Authorization;

public sealed class ApiKeyScopeAuthorizationHandlerTests
{
    private static ClaimsPrincipal PrincipalWith(params string[] scopes)
    {
        var claims = scopes.Select(s => new Claim(
            ApiKeyAuthenticationOptions.DefaultScopeClaimType, s));
        var identity = new ClaimsIdentity(claims, "test");
        return new ClaimsPrincipal(identity);
    }

    private static async Task<bool> EvaluateAsync(
        ApiKeyScopeRequirement requirement,
        ClaimsPrincipal principal)
    {
        var context = new AuthorizationHandlerContext(
            new[] { requirement }, principal, resource: null);
        await new ApiKeyScopeAuthorizationHandler().HandleAsync(context);
        return context.HasSucceeded;
    }

    [Fact]
    public async Task any_mode_succeeds_when_one_scope_is_held()
    {
        var requirement = new ApiKeyScopeRequirement(new[] { "a", "b" });
        Assert.True(await EvaluateAsync(requirement, PrincipalWith("b")));
    }

    [Fact]
    public async Task any_mode_fails_when_no_scope_is_held()
    {
        var requirement = new ApiKeyScopeRequirement(new[] { "a", "b" });
        Assert.False(await EvaluateAsync(requirement, PrincipalWith("c")));
    }

    [Fact]
    public async Task all_mode_succeeds_only_when_every_scope_is_held()
    {
        var requirement = new ApiKeyScopeRequirement(new[] { "a", "b" }, requireAll: true);
        Assert.True(await EvaluateAsync(requirement, PrincipalWith("a", "b", "c")));
        Assert.False(await EvaluateAsync(requirement, PrincipalWith("a")));
    }

    [Fact]
    public void requirement_rejects_empty_scopes()
        => Assert.Throws<ArgumentException>(() => new ApiKeyScopeRequirement(Array.Empty<string>()));
}
