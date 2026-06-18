namespace Moongazing.OrionLedger.Demo;

using Moongazing.OrionLedger;

/// <summary>
/// Issue a key and show the two halves of the lifecycle contract: the plaintext token is returned
/// exactly once, and storage holds only its SHA-256 hash.
/// </summary>
internal static class IssueAndHashDemo
{
    public static async Task RunAsync(IApiKeyService keys)
    {
        DemoConsole.Section("1. Issue a key (show-once token + stored hash)");

        var issued = await keys.IssueAsync("Acme Corp", scopes: ["orders:read", "orders:write"]);

        DemoConsole.Step("The plaintext token is surfaced ONCE, here:");
        DemoConsole.Detail("token (plaintext)", issued.Token);

        DemoConsole.Step("Storage never sees the plaintext. The record holds only a hash:");
        DemoConsole.Detail("id", issued.Record.Id);
        DemoConsole.Detail("name", issued.Record.Name);
        DemoConsole.Detail("displayPrefix", issued.Record.DisplayPrefix);
        DemoConsole.Detail("hash (SHA-256)", issued.Record.Hash);
        DemoConsole.Detail("scopes", string.Join(", ", issued.Record.Scopes));
        DemoConsole.Detail("createdAt", issued.Record.CreatedAt.ToString("O"));
        DemoConsole.Detail("expiresAt", issued.Record.ExpiresAt?.ToString("O") ?? "(never)");

        DemoConsole.Result("Token shown once; only a 64-char hex hash is persisted.");
    }
}
