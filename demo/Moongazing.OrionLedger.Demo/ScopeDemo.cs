namespace Moongazing.OrionLedger.Demo;

using Moongazing.OrionLedger;

/// <summary>
/// Scope checking. A key granted one scope is accepted when that scope is required, rejected with
/// MissingScope when a scope it lacks is required, and accepted when no scope is required at all.
/// </summary>
internal static class ScopeDemo
{
    public static async Task RunAsync(IApiKeyService keys)
    {
        DemoConsole.Section("3. Scope check (missing scope rejected)");

        var issued = await keys.IssueAsync("reporting-job", scopes: ["reports:read"]);
        DemoConsole.Step($"Issued a key granting only: {string.Join(", ", issued.Record.Scopes)}");

        DemoConsole.Step("Require a scope the key holds (reports:read):");
        var held = await keys.VerifyAsync(issued.Token, requiredScope: "reports:read");
        DemoConsole.PrintVerification(issued.Token, held);

        DemoConsole.Step("Require a scope the key lacks (reports:write):");
        var lacking = await keys.VerifyAsync(issued.Token, requiredScope: "reports:write");
        DemoConsole.PrintVerification(issued.Token, lacking);

        DemoConsole.Step("Require no scope at all (the check is skipped):");
        var noScope = await keys.VerifyAsync(issued.Token);
        DemoConsole.PrintVerification(issued.Token, noScope);

        DemoConsole.Result(
            $"held={held.Status}, lacking={lacking.Status}, no-required-scope={noScope.Status}.");
    }
}
