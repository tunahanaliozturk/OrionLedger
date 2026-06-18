namespace Moongazing.OrionLedger.Demo;

using Moongazing.OrionLedger;

/// <summary>
/// Expiry. A key issued with an expiry in the future verifies as Valid; a key whose expiry has
/// already passed verifies as Expired, and the matched record is still returned so the rejection
/// can be logged against a named key.
/// </summary>
internal static class ExpiryDemo
{
    public static async Task RunAsync(IApiKeyService keys)
    {
        DemoConsole.Section("4. Expiry (past-expiry key rejected)");

        var live = await keys.IssueAsync(
            "temp-integration",
            expiresAt: DateTimeOffset.UtcNow.AddHours(1));
        DemoConsole.Step("A key that expires in one hour verifies now:");
        var liveResult = await keys.VerifyAsync(live.Token);
        DemoConsole.PrintVerification(live.Token, liveResult);

        var stale = await keys.IssueAsync(
            "expired-integration",
            expiresAt: DateTimeOffset.UtcNow.AddSeconds(-1));
        DemoConsole.Step("A key whose expiry already passed is rejected, but the record is returned:");
        var staleResult = await keys.VerifyAsync(stale.Token);
        DemoConsole.PrintVerification(stale.Token, staleResult);
        DemoConsole.Detail("rejected key", staleResult.Record?.Name ?? "(none)");

        DemoConsole.Result($"live={liveResult.Status}, past-expiry={staleResult.Status}.");
    }
}
