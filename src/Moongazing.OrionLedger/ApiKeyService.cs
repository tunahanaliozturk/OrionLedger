namespace Moongazing.OrionLedger;

using Moongazing.OrionLedger.Diagnostics;
using Moongazing.OrionLedger.Keys;
using Moongazing.OrionLedger.Observers;
using Moongazing.OrionLedger.Storage;

/// <summary>
/// Default <see cref="IApiKeyService"/>. Generates prefixed high-entropy tokens, persists only
/// their hash, and resolves verification against expiry, revocation, and scope. Lifecycle events
/// are reported through the diagnostics meter and a fault-safe observer.
/// </summary>
public sealed class ApiKeyService : IApiKeyService
{
    private readonly IApiKeyStore store;
    private readonly ApiKeyOptions options;
    private readonly ApiKeyDiagnostics diagnostics;
    private readonly IApiKeyEventObserver observer;
    private readonly Func<DateTimeOffset> now;
    private readonly Func<string> newId;

    /// <summary>Create the service.</summary>
    /// <param name="store">The key store.</param>
    /// <param name="options">Issuance options. Validated on construction.</param>
    /// <param name="diagnostics">The shared metrics instance.</param>
    /// <param name="observer">The lifecycle observer, or null for none.</param>
    public ApiKeyService(
        IApiKeyStore store,
        ApiKeyOptions options,
        ApiKeyDiagnostics diagnostics,
        IApiKeyEventObserver? observer = null)
        : this(store, options, diagnostics, observer,
               now: () => DateTimeOffset.UtcNow,
               newId: () => Guid.NewGuid().ToString("N"))
    {
    }

    internal ApiKeyService(
        IApiKeyStore store,
        ApiKeyOptions options,
        ApiKeyDiagnostics diagnostics,
        IApiKeyEventObserver? observer,
        Func<DateTimeOffset> now,
        Func<string> newId)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(now);
        ArgumentNullException.ThrowIfNull(newId);
        options.Validate();

        this.store = store;
        this.options = options;
        this.diagnostics = diagnostics;
        this.observer = observer ?? NullApiKeyEventObserver.Instance;
        this.now = now;
        this.newId = newId;
    }

    /// <inheritdoc />
    public async Task<IssuedApiKey> IssueAsync(
        string name,
        IEnumerable<string>? scopes = null,
        DateTimeOffset? expiresAt = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        var token = Keys.ApiKeyGenerator.Generate(options.Prefix, options.SecretByteLength);
        var createdAt = now();
        var record = new ApiKeyRecord
        {
            Id = newId(),
            Name = name,
            DisplayPrefix = Keys.ApiKeyGenerator.DisplayPrefix(token),
            Hash = ApiKeyHasher.Hash(token),
            Scopes = scopes is null
                ? new HashSet<string>(StringComparer.Ordinal)
                : new HashSet<string>(scopes, StringComparer.Ordinal),
            CreatedAt = createdAt,
            ExpiresAt = expiresAt ?? (options.DefaultLifetime is { } life ? createdAt + life : null),
        };

        await store.AddAsync(record, cancellationToken).ConfigureAwait(false);
        diagnostics.Issued.Add(1);
        SafeObserve(() => observer.OnIssued(record));

        return new IssuedApiKey(token, record);
    }

    /// <inheritdoc />
    public async Task<ApiKeyVerification> VerifyAsync(
        string? token,
        string? requiredScope = null,
        CancellationToken cancellationToken = default)
    {
        var verification = await ResolveAsync(token, requiredScope, cancellationToken).ConfigureAwait(false);
        diagnostics.RecordVerification(StatusTag(verification.Status));
        SafeObserve(() => observer.OnVerified(verification));
        return verification;
    }

    /// <inheritdoc />
    public async Task<bool> RevokeAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);

        var record = await store.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (record is null || record.RevokedAt is not null)
        {
            return false;
        }

        record.RevokedAt = now();
        await store.UpdateAsync(record, cancellationToken).ConfigureAwait(false);
        diagnostics.Revoked.Add(1);
        SafeObserve(() => observer.OnRevoked(record));
        return true;
    }

    private async Task<ApiKeyVerification> ResolveAsync(string? token, string? requiredScope, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(token) || !token.StartsWith(options.Prefix, StringComparison.Ordinal))
        {
            return ApiKeyVerification.Malformed;
        }

        var record = await store.FindByHashAsync(ApiKeyHasher.Hash(token), cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return ApiKeyVerification.NotFound;
        }

        if (record.RevokedAt is not null)
        {
            return ApiKeyVerification.Revoked(record);
        }

        if (record.ExpiresAt is { } expiresAt && expiresAt <= now())
        {
            return ApiKeyVerification.Expired(record);
        }

        if (requiredScope is not null && !record.Scopes.Contains(requiredScope))
        {
            return ApiKeyVerification.MissingScope(record);
        }

        record.LastUsedAt = now();
        await store.UpdateAsync(record, cancellationToken).ConfigureAwait(false);
        return ApiKeyVerification.Valid(record);
    }

    private static string StatusTag(ApiKeyStatus status) => status switch
    {
        ApiKeyStatus.Valid => "valid",
        ApiKeyStatus.Malformed => "malformed",
        ApiKeyStatus.NotFound => "not_found",
        ApiKeyStatus.Expired => "expired",
        ApiKeyStatus.Revoked => "revoked",
        ApiKeyStatus.MissingScope => "missing_scope",
        _ => "unknown",
    };

    private static void SafeObserve(Action action)
    {
        try
        {
            action();
        }
#pragma warning disable CA1031 // observer is observability, not load-bearing
        catch (Exception)
#pragma warning restore CA1031
        {
            // An observer fault must never block the lifecycle operation.
        }
    }
}
