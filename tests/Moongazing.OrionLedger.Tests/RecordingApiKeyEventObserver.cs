namespace Moongazing.OrionLedger.Tests;

using Moongazing.OrionLedger.Keys;
using Moongazing.OrionLedger.Observers;

/// <summary>
/// Test observer that records every lifecycle callback in order, so tests can assert that the
/// service notifies observers correctly. Optionally throws to exercise the service's fault-safety.
/// </summary>
internal sealed class RecordingApiKeyEventObserver : IApiKeyEventObserver
{
    private readonly bool throwOnEveryCallback;

    public RecordingApiKeyEventObserver(bool throwOnEveryCallback = false)
    {
        this.throwOnEveryCallback = throwOnEveryCallback;
    }

    public List<ApiKeyRecord> Issued { get; } = [];

    public List<ApiKeyVerification> Verified { get; } = [];

    public List<ApiKeyRecord> Revoked { get; } = [];

    public void OnIssued(ApiKeyRecord record)
    {
        Issued.Add(record);
        Throw();
    }

    public void OnVerified(ApiKeyVerification verification)
    {
        Verified.Add(verification);
        Throw();
    }

    public void OnRevoked(ApiKeyRecord record)
    {
        Revoked.Add(record);
        Throw();
    }

    private void Throw()
    {
        if (throwOnEveryCallback)
        {
            throw new InvalidOperationException("observer fault");
        }
    }
}
