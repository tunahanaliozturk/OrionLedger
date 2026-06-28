namespace Moongazing.OrionLedger.AspNetCore.Authorization;

using Microsoft.AspNetCore.Authorization;

/// <summary>
/// An authorization requirement that an OrionLedger API key identity hold one or more scopes, emitted
/// as scope claims by <see cref="ApiKeyAuthenticationHandler"/>. The requirement is bound to a single
/// authentication scheme (<see cref="AuthenticationScheme"/>): only an identity authenticated by that
/// scheme can satisfy it, so a scope claim minted by a different scheme (a cookie or JWT, say) on the
/// same principal is ignored. With <see cref="RequireAll"/> false (the default) any one of the listed
/// scopes satisfies the requirement; with it true the principal's API key identity must hold every
/// listed scope.
/// </summary>
public sealed class ApiKeyScopeRequirement : IAuthorizationRequirement
{
    /// <summary>Create the requirement.</summary>
    /// <param name="scopes">
    /// The scopes to check. Must be non-empty and contain no null, empty, or whitespace-only entries.
    /// </param>
    /// <param name="scopeClaimType">The claim type scopes are emitted as.</param>
    /// <param name="requireAll">
    /// When false (default) any one scope satisfies the requirement; when true all are required.
    /// </param>
    /// <param name="authenticationScheme">
    /// The authentication scheme whose identity is allowed to satisfy the requirement. Defaults to
    /// <see cref="ApiKeyAuthenticationOptions.DefaultScheme"/>. Pass the matching scheme name when the
    /// API key handler is registered under a non-default scheme.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="scopes"/> is empty or contains a null/blank entry, or
    /// <paramref name="scopeClaimType"/> / <paramref name="authenticationScheme"/> is null or blank.
    /// </exception>
    public ApiKeyScopeRequirement(
        IEnumerable<string> scopes,
        string scopeClaimType = ApiKeyAuthenticationOptions.DefaultScopeClaimType,
        bool requireAll = false,
        string authenticationScheme = ApiKeyAuthenticationOptions.DefaultScheme)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        ArgumentException.ThrowIfNullOrEmpty(scopeClaimType);
        ArgumentException.ThrowIfNullOrEmpty(authenticationScheme);

        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var scope in scopes)
        {
            // A null or blank scope is almost always a wiring mistake (a missing constant, a bad
            // split). Silently dropping it would let an over-broad or under-specified policy slip
            // through, so reject it up front rather than at evaluation time.
            if (string.IsNullOrWhiteSpace(scope))
            {
                throw new ArgumentException(
                    "Scopes must not contain null, empty, or whitespace-only entries.",
                    nameof(scopes));
            }

            set.Add(scope);
        }

        if (set.Count == 0)
        {
            throw new ArgumentException("At least one scope must be supplied.", nameof(scopes));
        }

        Scopes = set;
        ScopeClaimType = scopeClaimType;
        RequireAll = requireAll;
        AuthenticationScheme = authenticationScheme;
    }

    /// <summary>The scopes the principal is checked against. Compared ordinally.</summary>
    public IReadOnlySet<string> Scopes { get; }

    /// <summary>The claim type scopes are read from.</summary>
    public string ScopeClaimType { get; }

    /// <summary>True to require all <see cref="Scopes"/>; false to require any one.</summary>
    public bool RequireAll { get; }

    /// <summary>
    /// The authentication scheme whose identity may satisfy the requirement. Scope claims carried by
    /// any other identity on the principal are not considered.
    /// </summary>
    public string AuthenticationScheme { get; }
}
