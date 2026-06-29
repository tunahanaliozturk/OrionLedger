namespace Moongazing.OrionLedger.AspNetCore.Tests;

using System.Security.Claims;

using Microsoft.AspNetCore.Authorization;

using Moongazing.OrionLedger.AspNetCore.Authorization;

public sealed class ApiKeyScopeAuthorizationHandlerTests
{
    // The scope claims under test belong to an identity authenticated by the API key scheme, so the
    // identity's AuthenticationType is the scheme the requirement binds to by default.
    private static ClaimsPrincipal ApiKeyPrincipalWith(params string[] scopes)
        => PrincipalWith(ApiKeyAuthenticationOptions.DefaultScheme, scopes);

    private static ClaimsPrincipal PrincipalWith(string authenticationType, params string[] scopes)
    {
        var claims = scopes.Select(s => new Claim(
            ApiKeyAuthenticationOptions.DefaultScopeClaimType, s));
        var identity = new ClaimsIdentity(claims, authenticationType);
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
        Assert.True(await EvaluateAsync(requirement, ApiKeyPrincipalWith("b")));
    }

    [Fact]
    public async Task any_mode_fails_when_no_scope_is_held()
    {
        var requirement = new ApiKeyScopeRequirement(new[] { "a", "b" });
        Assert.False(await EvaluateAsync(requirement, ApiKeyPrincipalWith("c")));
    }

    [Fact]
    public async Task all_mode_succeeds_only_when_every_scope_is_held()
    {
        var requirement = new ApiKeyScopeRequirement(new[] { "a", "b" }, requireAll: true);
        Assert.True(await EvaluateAsync(requirement, ApiKeyPrincipalWith("a", "b", "c")));
        Assert.False(await EvaluateAsync(requirement, ApiKeyPrincipalWith("a")));
    }

    [Fact]
    public async Task api_key_identity_with_scope_satisfies_requirement()
    {
        var requirement = new ApiKeyScopeRequirement(new[] { "orders:read" });
        Assert.True(await EvaluateAsync(requirement, ApiKeyPrincipalWith("orders:read")));
    }

    [Fact]
    public async Task same_scope_on_a_different_scheme_does_not_satisfy_requirement()
    {
        // The principal carries the scope claim, but on an identity authenticated by another scheme
        // (a cookie/JWT). The API key scope requirement must not be satisfiable by it.
        var requirement = new ApiKeyScopeRequirement(new[] { "orders:read" });
        var principal = PrincipalWith("Cookies", "orders:read");

        Assert.False(await EvaluateAsync(requirement, principal));
    }

    [Fact]
    public async Task only_the_api_key_identity_scope_counts_on_a_multi_scheme_principal()
    {
        // A composed principal: the API key identity lacks the scope, a different scheme's identity
        // holds it. The cross-scheme claim must not leak into the API key check.
        var requirement = new ApiKeyScopeRequirement(new[] { "orders:read" });

        var apiKeyIdentity = new ClaimsIdentity(
            new[] { new Claim(ApiKeyAuthenticationOptions.DefaultScopeClaimType, "reports:read") },
            ApiKeyAuthenticationOptions.DefaultScheme);
        var jwtIdentity = new ClaimsIdentity(
            new[] { new Claim(ApiKeyAuthenticationOptions.DefaultScopeClaimType, "orders:read") },
            "Bearer");
        var principal = new ClaimsPrincipal(new[] { apiKeyIdentity, jwtIdentity });

        Assert.False(await EvaluateAsync(requirement, principal));
    }

    [Fact]
    public async Task custom_bound_scheme_is_honored()
    {
        const string scheme = "OrionLedgerApiKey-Internal";
        var requirement = new ApiKeyScopeRequirement(
            new[] { "orders:read" }, authenticationScheme: scheme);

        Assert.True(await EvaluateAsync(requirement, PrincipalWith(scheme, "orders:read")));
        // The default scheme's identity must not satisfy a requirement bound to the custom scheme.
        Assert.False(await EvaluateAsync(requirement, ApiKeyPrincipalWith("orders:read")));
    }

    [Fact]
    public async Task custom_scope_claim_type_is_honored()
    {
        const string claimType = "urn:orionledger:scope";
        var requirement = new ApiKeyScopeRequirement(
            new[] { "orders:read" }, scopeClaimType: claimType);

        var identity = new ClaimsIdentity(
            new[] { new Claim(claimType, "orders:read") },
            ApiKeyAuthenticationOptions.DefaultScheme);
        var principal = new ClaimsPrincipal(identity);

        Assert.True(await EvaluateAsync(requirement, principal));
        // The same scope under the default claim type must not satisfy the custom-typed requirement.
        Assert.False(await EvaluateAsync(requirement, ApiKeyPrincipalWith("orders:read")));
    }

    [Fact]
    public void requirement_rejects_empty_scopes()
        => Assert.Throws<ArgumentException>(() => new ApiKeyScopeRequirement(Array.Empty<string>()));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void requirement_rejects_null_or_blank_scope_entries(string? scope)
        => Assert.Throws<ArgumentException>(
            () => new ApiKeyScopeRequirement(new[] { "orders:read", scope! }));

    [Fact]
    public void requirement_rejects_blank_authentication_scheme()
        => Assert.Throws<ArgumentException>(
            () => new ApiKeyScopeRequirement(new[] { "orders:read" }, authenticationScheme: ""));
}
