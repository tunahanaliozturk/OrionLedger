namespace Moongazing.OrionLedger.Demo;

using Moongazing.OrionLedger.Keys;
using Moongazing.OrionLedger.Observers;

/// <summary>
/// A fault-safe lifecycle observer that prints an audit line for each event. Registering this
/// before AddOrionLedger demonstrates the audit hook the library calls on issue, verify, and revoke.
/// </summary>
internal sealed class ConsoleAuditObserver : IApiKeyEventObserver
{
    public void OnIssued(ApiKeyRecord record) =>
        Console.WriteLine($"      [audit] issued    id={record.Id[..8]} name={record.Name}");

    public void OnVerified(ApiKeyVerification verification) =>
        Console.WriteLine($"      [audit] verified  status={verification.Status}");

    public void OnRevoked(ApiKeyRecord record) =>
        Console.WriteLine($"      [audit] revoked   id={record.Id[..8]} name={record.Name}");
}
