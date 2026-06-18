namespace Moongazing.OrionLedger.Demo;

using Moongazing.OrionLedger;

/// <summary>
/// Verify a valid presented key, then verify a wrong one and a garbage one. Shows that verification
/// collapses to a single status the caller can switch on, and that the matched record is returned
/// only when the token resolves to a known key.
/// </summary>
internal static class VerifyDemo
{
    public static async Task RunAsync(IApiKeyService keys)
    {
        DemoConsole.Section("2. Verify presented keys (valid, wrong, garbage)");

        var issued = await keys.IssueAsync("billing-service", scopes: ["invoices:read"]);

        DemoConsole.Step("Present the exact token that was issued:");
        var valid = await keys.VerifyAsync(issued.Token);
        DemoConsole.PrintVerification(issued.Token, valid);

        DemoConsole.Step("Present a wrong token with the right prefix (correct shape, unknown hash):");
        var wrong = "ork_live_this-is-not-a-real-secret-0000000000000000";
        var wrongResult = await keys.VerifyAsync(wrong);
        DemoConsole.PrintVerification(wrong, wrongResult);

        DemoConsole.Step("Present garbage that does not even carry the prefix:");
        var garbage = "hello world";
        var garbageResult = await keys.VerifyAsync(garbage);
        DemoConsole.PrintVerification(garbage, garbageResult);

        DemoConsole.Result(
            $"valid={valid.Status}, wrong={wrongResult.Status}, garbage={garbageResult.Status}.");
    }
}
