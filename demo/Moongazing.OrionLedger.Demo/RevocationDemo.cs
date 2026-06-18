namespace Moongazing.OrionLedger.Demo;

using Moongazing.OrionLedger;

/// <summary>
/// Revocation. A key verifies as Valid until it is revoked by id; afterwards it verifies as
/// Revoked. Revocation is idempotent: revoking an already-revoked key returns false.
/// </summary>
internal static class RevocationDemo
{
    public static async Task RunAsync(IApiKeyService keys)
    {
        DemoConsole.Section("5. Revocation (revoked key rejected, revoke is idempotent)");

        var issued = await keys.IssueAsync("rotating-app");
        var keyId = issued.Record.Id;

        DemoConsole.Step("Before revocation the key verifies:");
        var before = await keys.VerifyAsync(issued.Token);
        DemoConsole.PrintVerification(issued.Token, before);

        DemoConsole.Step($"Revoke by id ({keyId[..8]}...):");
        var revoked = await keys.RevokeAsync(keyId);
        DemoConsole.Detail("RevokeAsync", revoked.ToString());

        DemoConsole.Step("Revoke the same id again (idempotent):");
        var again = await keys.RevokeAsync(keyId);
        DemoConsole.Detail("RevokeAsync", again.ToString());

        DemoConsole.Step("After revocation the key is rejected:");
        var after = await keys.VerifyAsync(issued.Token);
        DemoConsole.PrintVerification(issued.Token, after);

        DemoConsole.Result(
            $"before={before.Status}, first-revoke={revoked}, second-revoke={again}, after={after.Status}.");
    }
}
