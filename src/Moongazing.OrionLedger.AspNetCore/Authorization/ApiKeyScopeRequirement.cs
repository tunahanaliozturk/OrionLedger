namespace Moongazing.OrionLedger.AspNetCore.Authorization;

using Microsoft.AspNetCore.Authorization;

/// <summary>
/// An authorization requirement that the current principal hold one or more scopes, emitted as
/// scope claims by <see cref="ApiKeyAuthenticationHandler"/>. With <see cref="RequireAll"/> false
/// (the default) any one of the listed scopes satisfies the requirement; with it true the principal
/// must hold every listed scope.
/// </summary>
public sealed class ApiKeyScopeRequirement : IAuthorizationRequirement
{
    /// <summary>Create the requirement.</summary>
    /// <param name="scopes">The scopes to check. Must be non-empty.</param>
    /// <param name="scopeClaimType">The claim type scopes are emitted as.</param>
    /// <param name="requireAll">
    /// When false (default) any one scope satisfies the requirement; when true all are required.
    /// </param>
    public ApiKeyScopeRequirement(
        IEnumerable<string> scopes,
        string scopeClaimType = ApiKeyAuthenticationOptions.DefaultScopeClaimType,
        bool requireAll = false)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        ArgumentException.ThrowIfNullOrEmpty(scopeClaimType);

        var set = new HashSet<string>(scopes, StringComparer.Ordinal);
        if (set.Count == 0)
        {
            throw new ArgumentException("At least one scope must be supplied.", nameof(scopes));
        }

        Scopes = set;
        ScopeClaimType = scopeClaimType;
        RequireAll = requireAll;
    }

    /// <summary>The scopes the principal is checked against. Compared ordinally.</summary>
    public IReadOnlySet<string> Scopes { get; }

    /// <summary>The claim type scopes are read from.</summary>
    public string ScopeClaimType { get; }

    /// <summary>True to require all <see cref="Scopes"/>; false to require any one.</summary>
    public bool RequireAll { get; }
}
