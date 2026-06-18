namespace Moongazing.OrionLedger.Observers;

using Moongazing.OrionLedger.Keys;

/// <summary>
/// Consumer hook notified about key lifecycle events, for audit logging and alerting.
/// Implementations are observability only: they must not throw, and the service swallows any
/// fault they raise so an observer outage never blocks issuance, verification, or revocation.
/// </summary>
public interface IApiKeyEventObserver
{
    /// <summary>Called after a key is issued.</summary>
    /// <param name="record">The issued record (without the plaintext token).</param>
    void OnIssued(ApiKeyRecord record);

    /// <summary>Called after every verification attempt.</summary>
    /// <param name="verification">The verification outcome.</param>
    void OnVerified(ApiKeyVerification verification);

    /// <summary>
    /// Called after a key is revoked, including once per key swept by bulk revocation.
    /// </summary>
    /// <param name="record">The revoked record.</param>
    void OnRevoked(ApiKeyRecord record);

    /// <summary>
    /// Called after a key is rotated. The predecessor's <see cref="ApiKeyRecord.SupersededById"/>
    /// links to the successor, whose record is supplied here.
    /// </summary>
    /// <remarks>
    /// A default interface method so observers written against 0.1.0 keep compiling and silently
    /// ignore rotation. Override it to audit rotations.
    /// </remarks>
    /// <param name="predecessor">The rotated key, now superseded.</param>
    /// <param name="successor">The freshly issued successor key.</param>
    void OnRotated(ApiKeyRecord predecessor, ApiKeyRecord successor)
    {
    }
}

/// <summary>A no-op observer used when the consumer registers none.</summary>
public sealed class NullApiKeyEventObserver : IApiKeyEventObserver
{
    /// <summary>The shared no-op instance.</summary>
    public static readonly NullApiKeyEventObserver Instance = new();

    private NullApiKeyEventObserver()
    {
    }

    /// <inheritdoc />
    public void OnIssued(ApiKeyRecord record)
    {
    }

    /// <inheritdoc />
    public void OnVerified(ApiKeyVerification verification)
    {
    }

    /// <inheritdoc />
    public void OnRevoked(ApiKeyRecord record)
    {
    }
}
