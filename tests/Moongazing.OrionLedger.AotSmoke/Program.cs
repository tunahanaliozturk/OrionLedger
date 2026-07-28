// NativeAOT smoke test. Publishing this with PublishAot=true must produce zero trim/AOT warnings,
// and running it must exit 0 - OrionLedger's AOT exit criterion. Runtime checks, not a framework:
// the point is to prove the API-key lifecycle (issue, verify, scope, revoke) survives trimming.
using Microsoft.Extensions.DependencyInjection;
using Moongazing.OrionLedger;

var services = new ServiceCollection();
services.AddOrionLedger();

using var provider = services.BuildServiceProvider();
var keys = provider.GetRequiredService<IApiKeyService>();

var issued = await keys.IssueAsync("smoke-key", scopes: ["orders:read"]);
Check(!string.IsNullOrEmpty(issued.Token), "issued key should carry a plaintext token");

var verified = await keys.VerifyAsync(issued.Token);
Check(verified.IsValid, $"the issued token should verify, status was {verified.Status}");

var inScope = await keys.VerifyAsync(issued.Token, requiredScope: "orders:read");
Check(inScope.IsValid, "the token should satisfy the orders:read scope");

var outOfScope = await keys.VerifyAsync(issued.Token, requiredScope: "orders:write");
Check(!outOfScope.IsValid, "the token should not satisfy the orders:write scope");

var revoked = await keys.RevokeAsync(issued.Record.Id);
Check(revoked, "revoke should succeed for a live key");

var afterRevoke = await keys.VerifyAsync(issued.Token);
Check(!afterRevoke.IsValid, $"a revoked token should not verify, status was {afterRevoke.Status}");

Console.WriteLine("OrionLedger AOT smoke test passed.");
return 0;

static void Check(bool condition, string message)
{
    if (!condition)
    {
        Console.Error.WriteLine($"AOT smoke test failed: {message}");
        Environment.Exit(1);
    }
}
