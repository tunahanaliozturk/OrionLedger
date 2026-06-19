namespace Moongazing.OrionLedger.Demo;

using Moongazing.OrionLedger;

/// <summary>
/// Key lifecycle management (v0.2): rotate a key with an optional grace window, last-verified
/// tracking on each successful verify, and bulk revoke of every active key for a subject.
/// </summary>
internal static class LifecycleDemo
{
    public static async Task RunAsync(IApiKeyService keys)
    {
        DemoConsole.Section("6. Lifecycle (rotate with grace, last-verified tracking, bulk revoke)");

        // Rotation with a grace window: the old token keeps verifying until it retires.
        var original = await keys.IssueAsync("billing-service", scopes: ["billing:read"], subject: "tenant-42");
        DemoConsole.Step($"Rotate the key with a 5-minute grace window ({original.Record.Id[..8]}...):");
        var rotation = await keys.RotateAsync(original.Record.Id, grace: TimeSpan.FromMinutes(5));
        DemoConsole.Detail("successor id", rotation!.Successor.Record.Id[..8] + "...");
        DemoConsole.Detail("predecessor", rotation.Predecessor.Id[..8] + "...");
        DemoConsole.Detail("retires at", rotation.Predecessor.RetiresAt?.ToString("u") ?? "(immediately)");

        DemoConsole.Step("During the grace window both the old and new tokens verify:");
        var oldStillWorks = await keys.VerifyAsync(original.Token);
        var newWorks = await keys.VerifyAsync(rotation.Token);
        DemoConsole.Detail("old token", oldStillWorks.Status.ToString());
        DemoConsole.Detail("new token", newWorks.Status.ToString());

        // Last-verified tracking: each successful verify stamps LastUsedAt and bumps LastUsedCount.
        DemoConsole.Step("Verify the new token a few times to advance last-verified tracking:");
        for (var i = 0; i < 3; i++)
        {
            await keys.VerifyAsync(rotation.Token);
        }

        var refreshed = await keys.VerifyAsync(rotation.Token);
        DemoConsole.Detail("LastUsedAt", refreshed.Record!.LastUsedAt?.ToString("u") ?? "(never)");
        DemoConsole.Detail("LastUsedCount", refreshed.Record!.LastUsedCount.ToString());

        // Bulk revoke: revoke every still-active key for a subject in one call.
        await keys.IssueAsync("billing-worker", subject: "tenant-42");
        DemoConsole.Step("Revoke every active key for subject 'tenant-42':");
        var revokedCount = await keys.RevokeAllForSubjectAsync("tenant-42");
        DemoConsole.Detail("keys revoked", revokedCount.ToString());

        var afterBulk = await keys.VerifyAsync(rotation.Token);
        DemoConsole.Detail("successor now", afterBulk.Status.ToString());

        DemoConsole.Result(
            $"rotated with grace, last-verified count={refreshed.Record!.LastUsedCount}, bulk-revoked={revokedCount} key(s).");
    }
}
