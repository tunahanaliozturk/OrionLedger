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
    public Task<IssuedApiKey> IssueAsync(
        string name,
        IEnumerable<string>? scopes = null,
        DateTimeOffset? expiresAt = null,
        string? subject = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        return IssueCoreAsync(
            name,
            subject,
            scopes is null
                ? new HashSet<string>(StringComparer.Ordinal)
                : new HashSet<string>(scopes, StringComparer.Ordinal),
            expiresAt,
            applyDefaultLifetime: true,
            cancellationToken);
    }

    private async Task<IssuedApiKey> IssueCoreAsync(
        string name,
        string? subject,
        IReadOnlySet<string> scopes,
        DateTimeOffset? expiresAt,
        bool applyDefaultLifetime,
        CancellationToken cancellationToken)
    {
        var token = Keys.ApiKeyGenerator.Generate(options.Prefix, options.SecretByteLength);
        var createdAt = now();
        var record = new ApiKeyRecord
        {
            Id = newId(),
            Name = name,
            Subject = subject,
            DisplayPrefix = Keys.ApiKeyGenerator.DisplayPrefix(token),
            Hash = ApiKeyHasher.Hash(token),
            Scopes = scopes,
            CreatedAt = createdAt,
            ExpiresAt = expiresAt
                ?? (applyDefaultLifetime && options.DefaultLifetime is { } life ? createdAt + life : null),
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

        await RevokeRecordAsync(record, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <inheritdoc />
    public async Task<KeyRotation?> RotateAsync(
        string id,
        TimeSpan? grace = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        if (grace is { } g && g < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(grace), grace, "Grace must not be negative.");
        }

        var predecessor = await store.FindByIdAsync(id, cancellationToken).ConfigureAwait(false);

        // Only an active, unexpired, not-yet-rotated key can be rotated. Rotating anything else is a
        // no-op so a caller cannot fork a chain off a dead key or rotate the same key twice.
        if (predecessor is null
            || predecessor.RevokedAt is not null
            || predecessor.SupersededById is not null
            || (predecessor.ExpiresAt is { } expiresAt && expiresAt <= now()))
        {
            return null;
        }

        // The successor inherits identity and grant: same name, subject, scopes, and (absolute)
        // expiry. Default lifetime is not re-applied, so a non-expiring key stays non-expiring.
        var successor = await IssueCoreAsync(
            predecessor.Name,
            predecessor.Subject,
            predecessor.Scopes,
            predecessor.ExpiresAt,
            applyDefaultLifetime: false,
            cancellationToken).ConfigureAwait(false);

        var rotatedAt = now();
        predecessor.SupersededAt = rotatedAt;
        predecessor.SupersededById = successor.Record.Id;

        if (grace is { } window && window > TimeSpan.Zero)
        {
            // Grace window: the old key keeps verifying until it retires.
            predecessor.RetiresAt = rotatedAt + window;
        }
        else
        {
            // No grace: retire immediately by revoking the predecessor now.
            predecessor.RevokedAt = rotatedAt;
        }

        await store.UpdateAsync(predecessor, cancellationToken).ConfigureAwait(false);
        diagnostics.Rotated.Add(1);
        SafeObserve(() => observer.OnRotated(predecessor, successor.Record));

        return new KeyRotation(successor, predecessor);
    }

    /// <inheritdoc />
    public async Task<int> RevokeAllForSubjectAsync(string subject, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(subject);

        var records = await store.FindBySubjectAsync(subject, cancellationToken).ConfigureAwait(false);

        var revoked = 0;
        foreach (var record in records)
        {
            if (!IsActive(record))
            {
                continue;
            }

            await RevokeRecordAsync(record, cancellationToken).ConfigureAwait(false);
            revoked++;
        }

        return revoked;
    }

    // An already-inactive key (revoked, rotation-retired, or expired) is left untouched so bulk
    // revocation only ends *active* keys and never rewrites the historical outcome of an inactive one.
    private bool IsActive(ApiKeyRecord record)
    {
        var current = now();
        return record.RevokedAt is null
            && (record.RetiresAt is not { } retiresAt || retiresAt > current)
            && (record.ExpiresAt is not { } expiresAt || expiresAt > current);
    }

    private async Task RevokeRecordAsync(ApiKeyRecord record, CancellationToken cancellationToken)
    {
        record.RevokedAt = now();
        await store.UpdateAsync(record, cancellationToken).ConfigureAwait(false);
        diagnostics.Revoked.Add(1);
        SafeObserve(() => observer.OnRevoked(record));
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

        // A rotated key with an elapsed grace window is retired. Checked before expiry so a key that
        // is both retired and past its (inherited) expiry reports the rotation outcome.
        if (record.RetiresAt is { } retiresAt && retiresAt <= now())
        {
            return ApiKeyVerification.Retired(record);
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
        record.LastUsedCount++;
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
        ApiKeyStatus.Retired => "retired",
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
