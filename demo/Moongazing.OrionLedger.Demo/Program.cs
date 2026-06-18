namespace Moongazing.OrionLedger.Demo;

using Microsoft.Extensions.DependencyInjection;

using Moongazing.OrionLedger;
using Moongazing.OrionLedger.Observers;

/// <summary>
/// Runnable walkthrough of the OrionLedger API key lifecycle over the in-memory store, using the
/// real ApiKeyService resolved from DI. Runs to completion with no input.
/// </summary>
internal static class Program
{
    private static async Task<int> Main()
    {
        Console.WriteLine("OrionLedger demo - API key lifecycle (issue / hash / verify / scope / expiry / revoke)");
        Console.WriteLine("Store: in-memory (process-local). Service: the real ApiKeyService via AddOrionLedger.");

        // Register a fault-safe audit observer BEFORE AddOrionLedger so the library wires it in.
        // AddOrionLedger adds the in-memory store and the real ApiKeyService.
        var services = new ServiceCollection();
        services.AddSingleton<IApiKeyEventObserver, ConsoleAuditObserver>();
        services.AddOrionLedger(o =>
        {
            o.Prefix = "ork_live_";
            o.DefaultLifetime = TimeSpan.FromDays(90);
        });

        using var provider = services.BuildServiceProvider();
        var keys = provider.GetRequiredService<IApiKeyService>();

        await IssueAndHashDemo.RunAsync(keys);
        await VerifyDemo.RunAsync(keys);
        await ScopeDemo.RunAsync(keys);
        await ExpiryDemo.RunAsync(keys);
        await RevocationDemo.RunAsync(keys);

        Console.WriteLine();
        Console.WriteLine(new string('=', 72));
        Console.WriteLine("  Demo complete. All lifecycle stages ran to completion.");
        Console.WriteLine(new string('=', 72));
        return 0;
    }
}
